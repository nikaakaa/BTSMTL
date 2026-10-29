import type { GraphEdge } from '../../api/types';
import { isEdgeOccurrence, isNodeOccurrence, isPortSample, type LaidNode, type RouteRecord } from '../../state/graphModel';

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
  signature: string;
}

const idleNode: NodeState = { occurred: 0, visited: false, failed: false, ports: {}, detail: '', signature: '' };
const idleEdge: EdgeState = { occurrences: 0, selected: false, passed: false, traveled: false, rejected: false, signature: '' };

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

  update(nodes: LaidNode[], edges: GraphEdge[], records: RouteRecord[], history: RouteHistory) {
    for (const { node } of nodes) {
      const events = records.filter((item): item is Extract<RouteRecord, { kind: string }> => isNodeOccurrence(item) && item.nodeId === node.id)
        .sort((left, right) => Number(left.sequence) - Number(right.sequence));
      const ports: Record<string, PortValue> = {};
      for (const sample of records)
        if (isPortSample(sample) && sample.nodeId === node.id)
          ports[`${sample.direction}:${sample.portId}`] = { value: sample.value, valueType: sample.valueType, sequence: sample.sequence };
      const detail = events.map(item => `${item.sequence} · ${item.kind} · ${item.result}`).join('\n');
      const visited = history.visited.has(node.id);
      const next: NodeState = {
        occurred: events.length, visited, failed: events.some(item => item.result === 'Failure'), ports, detail,
        signature: `${visited}|${detail}|${Object.entries(ports).map(([key, port]) => `${key}=${port.value}`).join(',')}`,
      };
      if (next.signature === this.node(node.id).signature) continue;
      this.nodes.set(node.id, next);
      this.notify(`node:${node.id}`);
    }
    for (const edge of edges) {
      const events = records.filter(item => isEdgeOccurrence(item) && item.edgeId === edge.id) as Extract<RouteRecord, { edgeId: string }>[];
      const selected = events.some(item => item.selected), passed = events.some(item => item.passed);
      const rejected = events.length > 0 && events.every(item => !item.passed);
      const traveled = history.traveled.has(edge.id);
      const next: EdgeState = {
        occurrences: events.length, selected, passed, traveled, rejected,
        signature: `${events.length}|${selected}|${passed}|${traveled}|${rejected}`,
      };
      if (next.signature === this.edge(edge.id).signature) continue;
      this.edges.set(edge.id, next);
      this.notify(`edge:${edge.id}`);
    }
  }
}
