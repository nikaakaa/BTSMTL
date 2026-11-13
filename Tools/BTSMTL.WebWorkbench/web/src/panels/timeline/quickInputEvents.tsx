// 快速档的图外事件：作者在输入轨按含义放事件（宿主声明的读取），快速路线只在宿主未声明含义的读取上假设，
// 假设处在输入轨上画标记，点开可直接为它放事件或打开相关转移。
import { useMemo } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { PreviewQuickPlan } from '../../api/previewQuickRecords';
import { quickInputSourceKey } from '../../state/quickPreview';
import { useAuthoring } from '../../state/authoring';
import { consoleActions, useWorkbench } from '../../state/store';
import { inputAuthoring } from '../../state/authoringInputs';
import { useQuickConditionName } from '../../state/quickConditionName';
import type { MenuEntry } from '../../ui/controls';
import { locateQuickSource, useQuickName, useQuickPlan } from './QuickPreview';

type Write = { key: string; label: string; satisfied: boolean };
type Condition = PreviewQuickPlan['conditions'][number];

// 当前快预览输入方案的输入 Timeline；只有打开该入口文档的作者模式下才能写。
function useQuickInput() {
  const input = useWorkbench(useShallow(state => {
    const description = state.quickPreview.description;
    const root = description.kind === 'ready' ? description.reply.data?.root ?? null : null;
    return { root: root?.authoring.documentPath ?? null, timeline: root?.inputSchemes
      .find(item => quickInputSourceKey(item.source) === quickInputSourceKey(state.quickPreview.scheme))?.timelineId ?? null };
  }));
  const editable = useAuthoring(state => state.author && state.target?.documentPath === input.root);
  return { timeline: input.timeline, editable };
}

function write(timeline: string | null, seconds: number, writes: Write[]) {
  void inputAuthoring.writeConditions(timeline, seconds, writes).catch(error => consoleActions.error('快预览', error));
}

const blockedTip = '进入作者模式并打开这个文档后才能放置';

// 输入轨菜单里的外部事件：选择组每个取值一项（选中项成立、同组其它不成立）；独立含义给出与默认值相反的一项，
// 只能假设的读取成立、不成立都给。
export function useQuickEventEntries(documentId: string, seconds: () => number): MenuEntry[] {
  const plan = useQuickPlan();
  const { timeline, editable } = useQuickInput();
  const name = useQuickConditionName();
  return useMemo(() => {
    if (plan === null || timeline === null || timeline !== documentId || plan.conditions.length === 0) return [];
    const disabled = !editable;
    const tip = editable ? undefined : blockedTip;
    const item = (label: string, writes: Write[]): MenuEntry => ({ label, disabled, tip, onSelect: () => write(timeline, seconds(), writes) });
    const groups = new Map<string, Condition[]>();
    const single: Condition[] = [];
    for (const condition of plan.conditions)
      if (condition.group === null) single.push(condition);
      else groups.set(condition.group, [...groups.get(condition.group) ?? [], condition]);
    const entries: MenuEntry[] = [{ kind: 'separator' }, { kind: 'label', label: '外部事件' }];
    for (const options of groups.values())
      for (const option of options)
        entries.push(item(name(option.key, option.label),
          options.map(other => ({ key: other.key, label: name(other.key, other.label), satisfied: other === option }))));
    for (const condition of single) {
      const label = name(condition.key, condition.label);
      if (condition.undeterminable || !condition.defaultSatisfied) entries.push(item(`${label}：成立`, [{ key: condition.key, label, satisfied: true }]));
      if (condition.undeterminable || condition.defaultSatisfied) entries.push(item(`${label}：不成立`, [{ key: condition.key, label, satisfied: false }]));
    }
    return entries;
  }, [plan, timeline, documentId, editable, name, seconds]);
}

export type QuickAssumption = { key: string; seconds: number; entries: MenuEntry[]; tip: string };

// 路线上的假设处：同一时刻同一来源状态的去向合成一处，菜单按去向列出转移和它依赖的假设读取（成立／不成立）。
export function useQuickAssumptions(documentId: string, origin: number): QuickAssumption[] {
  const plan = useQuickPlan();
  const { timeline, editable } = useQuickInput();
  const condition = useQuickConditionName();
  const state = useQuickName();
  return useMemo(() => {
    if (plan === null || timeline === null || timeline !== documentId) return [];
    const points = new Map<string, PreviewQuickPlan['suggestions']>();
    for (const suggestion of plan.suggestions) {
      if (suggestion.branch === 'None') continue;
      const key = `${suggestion.start}|${suggestion.fromState}|${suggestion.fromSource?.nodeId ?? ''}`;
      points.set(key, [...points.get(key) ?? [], suggestion]);
    }
    return [...points.entries()].map(([key, choices]) => {
      const seconds = choices[0].start;
      const at = Math.max(0, seconds - origin);
      const reads = new Set<string>();
      const entries: MenuEntry[] = [];
      // 按去向分组：每条转移下列它依赖的假设读取，同名条件图因此按去向区分。
      choices.forEach((choice, index) => {
        if (index !== 0) entries.push({ kind: 'separator' });
        entries.push({ kind: 'radio', checked: choice.branch === 'Taken', label: `转移到 ${state(choice.toSource, choice.toState)}`,
          onSelect: () => void locateQuickSource(choice.transitionSource).catch(error => consoleActions.error('快预览', error)) });
        for (const read of choice.missing) {
          const label = condition(read.key, read.label);
          reads.add(label);
          entries.push({ label: `${label}：成立`, disabled: !editable, tip: editable ? undefined : blockedTip,
            onSelect: () => write(timeline, at, [{ key: read.key, label, satisfied: true }]) });
          entries.push({ label: `${label}：不成立`, disabled: !editable, tip: editable ? undefined : blockedTip,
            onSelect: () => write(timeline, at, [{ key: read.key, label, satisfied: false }]) });
        }
      });
      return { key, seconds, entries, tip: `假设：${[...reads].join('、')}` };
    });
  }, [plan, timeline, documentId, editable, condition, state, origin]);
}
