import { compareSequence, isEdgeOccurrence, isNodeOccurrence, isPortSample, nodeOccurrenceLabel, type RouteRecord } from '../../state/graphModel';

// 每个节点图实例一份：按节点、连线保存本帧路线状态，只通知状态真正变了的节点与连线（D9）。

export interface PortValue {
  value: string;
  valueType: string;
  sequence: string;
}

export interface NodeState {
  occurred: number;
  // 本帧之前的路线已进入过。
  visited: boolean;
  failed: boolean;
  // 键为 `${direction}:${portId}`，取本帧最后一个样本。
  ports: Record<string, PortValue>;
  detail: string;
  signature: string;
}

export interface EdgeState {
  occurrences: number;
  selected: boolean;
  passed: boolean;
  // 本帧之前的路线已通过。
  traveled: boolean;
  rejected: boolean;
  detail: string;
  signature: string;
}

const idleNode: NodeState = { occurred: 0, visited: false, failed: false, ports: {}, detail: '', signature: '' };
const idleEdge: EdgeState = { occurrences: 0, selected: false, passed: false, traveled: false, rejected: false, detail: '', signature: '' };

export interface RouteHistory {
  visited: Set<string>;
  traveled: Set<string>;
}

export class RouteStore {
  private nodes = new Map<string, NodeState>();
  private edges = new Map<string, EdgeState>();
  private listeners = new Map<string, Set<() => void>>();
  private subscribers = new Map<string, (listener: () => void) => () => void>();

  node = (id: string): NodeState => this.nodes.get(id) ?? idleNode;
  edge = (id: string): EdgeState => this.edges.get(id) ?? idleEdge;

  // 每个身份返回同一个订阅函数，供 useSyncExternalStore 稳定使用。
  subscribe(key: string) {
    let subscriber = this.subscribers.get(key);
    if (!subscriber) {
      subscriber = listener => {
        let set = this.listeners.get(key);
        if (!set) this.listeners.set(key, set = new Set());
        set.add(listener);
        return () => { set!.delete(listener); };
      };
      this.subscribers.set(key, subscriber);
    }
    return subscriber;
  }

  private notify(key: string) {
    this.listeners.get(key)?.forEach(listener => listener());
  }

  update(records: RouteRecord[], history: RouteHistory) {
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
      const ports: Record<string, PortValue> = {};
      for (const sample of own.filter(isPortSample)) {
        const key = `${sample.direction}:${sample.portId}`;
        const previous = ports[key];
        if (previous === undefined || BigInt(sample.sequence) > BigInt(previous.sequence))
          ports[key] = { value: sample.value, valueType: sample.valueType, sequence: sample.sequence };
      }
      const visited = history.visited.has(id);
      if (own.length === 0 && !visited) {
        this.nodes.delete(id);
        this.notify(`node:${id}`);
        continue;
      }
      const detail = events.map(item => `${item.sequence} · ${nodeOccurrenceLabel(item)} · ${item.result ?? '结果未记录'}`).join('\n');
      const next: NodeState = {
        occurred: events.length, visited, failed: events.some(item => item.result === 'Failure'), ports, detail,
        signature: JSON.stringify([visited, detail, ports]),
      };
      if (next.signature === this.node(id).signature) continue;
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
      const detail = events.map(item => `${item.sequence} · ${item.selected ? '已选择' : '未选择'} · ${item.passed ? '通过' : '未通过'}`).join('\n');
      const next: EdgeState = {
        occurrences: events.length, selected, passed, traveled, rejected, detail,
        signature: JSON.stringify([traveled, detail]),
      };
      if (next.signature === this.edge(id).signature) continue;
      this.edges.set(id, next);
      this.notify(`edge:${id}`);
    }
  }
}
