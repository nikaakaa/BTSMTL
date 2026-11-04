import type { EdgeOccurrence, NodeOccurrence, PortSample } from '../../api/types';
import { compareSequence, isEdgeOccurrence, isNodeOccurrence, isPortSample, loopScopeLabel, nodeOccurrenceLabel, type RouteRecord } from '../../state/graphModel';

// 每个节点图实例一份：按节点、连线保存本帧路线状态，只通知状态真正变了的节点与连线（D9）。

export interface NodeState {
  occurred: number;
  // 本帧之前的路线已进入过。
  visited: boolean;
  failed: boolean;
  events: NodeOccurrence[];
  ports: Record<string, PortSample[]>;
  detail: string;
  signature: string;
}

export interface EdgeState {
  events: EdgeOccurrence[];
  occurrences: number;
  selected: boolean;
  passed: boolean;
  // 本帧之前的路线已通过。
  traveled: boolean;
  rejected: boolean;
  detail: string;
  signature: string;
}

const idleNode: NodeState = { occurred: 0, visited: false, failed: false, events: [], ports: {}, detail: '', signature: '' };
const idleEdge: EdgeState = { events: [], occurrences: 0, selected: false, passed: false, traveled: false, rejected: false, detail: '', signature: '' };

export interface RouteHistory {
  visited: Set<string>;
  traveled: Set<string>;
}

export class RouteStore {
  private scope: string | null = null;
  private nodes = new Map<string, NodeState>();
  private edges = new Map<string, EdgeState>();
  private listeners = new Map<string, Set<() => void>>();
  private subscribers = new Map<string, (listener: () => void) => () => void>();

  node = (id: string, scope: string): NodeState => scope === this.scope ? this.nodes.get(id) ?? idleNode : idleNode;
  edge = (id: string, scope: string): EdgeState => scope === this.scope ? this.edges.get(id) ?? idleEdge : idleEdge;

  // 每个身份返回同一个订阅函数，供 useSyncExternalStore 稳定使用。
  subscribe(key: string) {
    let subscriber = this.subscribers.get(key);
    if (!subscriber) {
      subscriber = listener => {
        let set = this.listeners.get(key);
        if (!set) this.listeners.set(key, set = new Set());
        set.add(listener);
        const bucket = set;
        return () => { bucket.delete(listener); };
      };
      this.subscribers.set(key, subscriber);
    }
    return subscriber;
  }

  private notify(key: string) {
    this.listeners.get(key)?.forEach(listener => listener());
  }

  update(records: RouteRecord[], history: RouteHistory, scope: string) {
    const changedScope = scope !== this.scope;
    this.scope = scope;
    const nodeRecords = new Map<string, RouteRecord[]>(), edgeRecords = new Map<string, RouteRecord[]>();
    for (const record of records) {
      const groups = isEdgeOccurrence(record) ? edgeRecords : nodeRecords;
      const id = isEdgeOccurrence(record) ? record.edgeId : record.nodeId;
      const group = groups.get(id);
      if (group === undefined) groups.set(id, [record]);
      else group.push(record);
    }
    for (const id of new Set([...this.nodes.keys(), ...nodeRecords.keys(), ...history.visited])) {
      const own = nodeRecords.get(id) ?? [];
      const events = own.filter(isNodeOccurrence).sort(compareSequence);
      const ports: Record<string, PortSample[]> = {};
      for (const sample of own.filter(isPortSample)) {
        const key = `${sample.direction}:${sample.portId}`;
        const samples = ports[key];
        if (samples === undefined) ports[key] = [sample];
        else samples.push(sample);
      }
      const visited = history.visited.has(id);
      if (own.length === 0 && !visited) {
        this.nodes.delete(id);
        this.notify(`node:${id}`);
        continue;
      }
      const detail = events.map(item => `${item.sequence} · ${nodeOccurrenceLabel(item)} · ${item.result ?? '结果未记录'}` +
        (item.loopScope === null ? '' : ` · ${loopScopeLabel(item.loopScope)}`) +
        (item.activationAvailability?.reason ? ` · ${item.activationAvailability.reason}` : '') +
        (item.stopCause === null ? '' : ` · StopCause ${item.stopCause}`) +
        (item.stopMode === null ? '' : ` · StopMode ${item.stopMode}`)).join('\n');
      const next: NodeState = {
        occurred: events.length, visited, failed: events.some(item => item.result === 'Failure'), events, ports, detail,
        signature: JSON.stringify([visited, events, ports]),
      };
      if (!changedScope && next.signature === this.node(id, scope).signature) continue;
      this.nodes.set(id, next);
      this.notify(`node:${id}`);
    }
    for (const id of new Set([...this.edges.keys(), ...edgeRecords.keys(), ...history.traveled])) {
      const events = (edgeRecords.get(id) ?? []).filter(isEdgeOccurrence).sort(compareSequence);
      const selected = events.some(item => item.selected), passed = events.some(item => item.passed);
      const rejected = events.length > 0 && events.every(item => !item.passed);
      const traveled = history.traveled.has(id);
      if (events.length === 0 && !traveled) {
        this.edges.delete(id);
        this.notify(`edge:${id}`);
        continue;
      }
      const detail = events.map(item => `${item.sequence} · 来源节点 ${item.sourceNodeId ?? '未记录'} · 激活 ${item.generation ?? '未提供'}` +
        ` · ${item.selected ? '已选择' : '未选择'} · ${item.passed ? '通过' : '未通过'}` +
        (item.loopScope === null ? '' : ` · ${loopScopeLabel(item.loopScope)}`)).join('\n');
      const next: EdgeState = {
        events, occurrences: events.length, selected, passed, traveled, rejected, detail,
        signature: JSON.stringify([traveled, events]),
      };
      if (!changedScope && next.signature === this.edge(id, scope).signature) continue;
      this.edges.set(id, next);
      this.notify(`edge:${id}`);
    }
  }
}
