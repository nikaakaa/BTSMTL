import { useCallback } from 'react';
import { graphPath, useAuthoring } from './authoring';
import { nodesOf } from './graphDocument';
import { nodeName } from './graphModel';
import { parseSkillJson, type JsonObject } from './skillJson';

const identityLike = /^([0-9a-f]{32}|[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})$/i;

// 快预览条件的作者可读名：静态推导不认识的谓词只带“所在图:节点”身份，按已打开文档里的同一节点
// 取“所在图名 · 节点名”，并附上节点上填写的文字值（如激活入口 Heavy），同名节点因此能区分；找不到时保留原标签。
export function useQuickConditionName() {
  const documents = useAuthoring(state => state.documents);
  return useCallback((key: string, label: string) => {
    const match = /^Predicate:([^:]+):(.+)$/.exec(key);
    if (match === null) return label;
    const [, graphId, nodeId] = match;
    for (const document of Object.values(documents)) {
      const graph = document.documentGraphs?.find(item => item.id === graphId);
      const node = graph?.nodes.find(item => item.id === nodeId);
      if (node === undefined) continue;
      const path = graphPath(graphId);
      const text = document.edited[path] ?? (document.files[path] === undefined ? null : parseSkillJson(document.files[path]));
      const stored = text === null ? undefined : nodesOf(text).find(item => item.UID === nodeId) as JsonObject | undefined;
      const name = nodeName(node);
      const values = stored === undefined ? [] : Object.entries(stored)
        .filter(([field, value]) => field.startsWith('m_') && typeof value === 'string' && value.length !== 0 &&
          value !== name && !identityLike.test(value))
        .map(([, value]) => value as string);
      const own = values.length === 0 ? name : `${name}（${values.join('、')}）`;
      return graph?.displayName && graph.displayName !== name ? `${graph.displayName} · ${own}` : own;
    }
    return label;
  }, [documents]);
}
