import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
  authoringApi, type AuthoringTarget, type ComputedField, type DescribedPort, type FieldMetadata,
  type GraphReferenceTarget, type ReferenceChoice, type StorageShape, type StoredField, type TypeDescription,
} from '../../api/authoring';
import { ApiError, isAbort } from '../../api/http';
import {
  authoringActions, authoringDocument, graphAuthoring, graphPath, inputAuthoring, timelinePath, useAuthoring, useCurrentDocument, useInputContext, useSkillElements, variableTypes,
  type EdgeStep, type JsonKey, type JsonMutation,
} from '../../state/authoring';
import { JsonFloat, cloneJson, fpRaw, fpSeconds, getIn, numberOf, parseSkillJson, setIn, stringifySkillJson, type JsonObject, type JsonValue } from '../../state/skillJson';
import { adapterCatalogCache, useAdapterCatalog } from '../../state/adapterCatalog';
import { consoleActions, selectedScene, useWorkbench } from '../../state/store';
import { Facts, Fold } from '../../ui/blocks';
import { Button, Checkbox, FieldLabel, NumberInput, Select, TextInput, Tip } from '../../ui/controls';
import { AddIcon, CloseIcon, MoveIcon } from '../../ui/icons';
import { CurveEditor, isCurve } from './CurveEditor';
import { ReferencePicker } from './ReferencePicker';
import { openPanel } from '../../shell/layout';
import type { ReferenceCatalog } from '../../state/referenceSelection';
import { resourceObjectValue } from '../../api/resourceRegistration';
import { ResourceFieldPicker, ResourceScopeContext } from './ResourceFieldPicker';
import styles from './inspector.module.css';

// 检查器字段编辑（9.3）：节点字段按宿主类型描述（C# 作者字段合同）给出的值种类编辑，
// 片段按 JSON 自身的值类型编辑。写入作者 JSON 的编辑副本，保存时由宿主正式编译判断合法性。

type SetValue = (keys: JsonKey[], value: JsonValue) => void;
type SetFields = (changes: readonly JsonMutation[]) => void;
type ReferenceOwner = { kind: 'node'; graphId: string; nodeId: string }
  | { kind: 'value'; graphId: string | null; identity: string };

const sameValue = (left: JsonValue | undefined, right: JsonValue) =>
  left !== undefined && stringifySkillJson(left, '\n') === stringifySkillJson(right, '\n');

const valueText = (value: JsonValue | undefined) => value == null ? '空'
  : typeof value === 'object' ? stringifySkillJson(value, ' ') : String(value);

// 字段显示名照 Unity ObjectNames.NicifyVariableName：去掉 m_／_／k 前缀，按大小写断词，首字母大写（HasStart → Has Start）。
const memberLabel = (member: string) => {
  const words = member.replace(/^(m_|_|k(?=[A-Z]))/, '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z])([A-Z][a-z])/g, '$1 $2');
  return words.charAt(0).toUpperCase() + words.slice(1);
};

// ── 基本输入 ──

function Row({ label, tip, children }: { label: string; tip?: string; children: ReactNode }) {
  return <div className={styles.field}><FieldLabel label={label} tip={tip} /><div className={styles.editor}>{children}</div></div>;
}

export function ConfigurationFields({ value, onSet }: { value: JsonObject; onSet(value: JsonObject): void }) {
  return <>{Object.entries(value).map(([member, current]) => <ValueEditor key={member} label={memberLabel(member)} value={current}
    shape={null} keys={[member]} onSet={(keys, next) => {
      const configuration = cloneJson(value); setIn(configuration, keys, next); onSet(configuration);
    }} />)}</>;
}

// 文本：失焦或回车提交；空文本按存储类型写空字符串或 null。
function TextValue({ value, label, nullable, onSet }: {
  value: string | null; label: string; nullable?: boolean; onSet(value: JsonValue): void;
}) {
  const [draft, setDraft] = useState(value ?? '');
  useEffect(() => setDraft(value ?? ''), [value]);
  const commit = () => {
    if (draft === (value ?? '')) return;
    onSet(draft === '' && nullable ? null : draft);
  };
  return (
    <TextInput aria-label={label} value={draft} placeholder={value === null ? '空' : undefined}
      onChange={event => setDraft(event.currentTarget.value)} onBlur={commit}
      onKeyDown={event => { if (event.key === 'Enter') commit(); if (event.key === 'Escape') setDraft(value ?? ''); }} />
  );
}

function NumberValue({ value, label, integer, onSet }: { value: JsonValue | undefined; label: string; integer: boolean; onSet(value: JsonValue): void }) {
  const number = numberOf(value);
  return (
    <NumberInput wide label={label} value={Number.isNaN(number) ? null : number} step={integer ? 1 : undefined}
      onChange={next => { if (next !== null) onSet(integer ? Math.round(next) : new JsonFloat(next)); }} />
  );
}

function IntegerValue({ value, label, onSet }: { value: JsonValue | undefined; label: string; onSet(value: JsonValue): void }) {
  const text = value == null ? '' : String(value);
  const [draft, setDraft] = useState(text);
  const [issue, setIssue] = useState<string | null>(null);
  useEffect(() => { setDraft(text); setIssue(null); }, [text]);
  const commit = () => {
    if (draft === text) return;
    if (!/^-?(0|[1-9]\d*)$/.test(draft)) {
      setIssue('请输入十进制整数');
      return;
    }
    onSet(parseSkillJson(draft));
    setIssue(null);
  };
  return <>
    <TextInput aria-label={label} inputMode="numeric" value={draft} onChange={event => setDraft(event.currentTarget.value)}
      onBlur={commit} onKeyDown={event => {
        if (event.key === 'Enter') commit();
        if (event.key === 'Escape') { setDraft(text); setIssue(null); }
      }} />
    {issue && <span role="alert" className={styles.issue}>{issue}</span>}
  </>;
}

function VectorValue({ value, axes, label, onSet }: { value: JsonValue | undefined; axes: string[]; label: string; onSet: SetValue }) {
  const vector = (value ?? {}) as JsonObject;
  return (
    <div className={styles.vector}>
      {axes.map(axis => (
        <label key={axis} className={styles.axis}>
          <span>{axis.toUpperCase()}</span>
          <NumberValue value={vector[axis]} label={`${label} ${axis}`} integer={false} onSet={next => onSet([axis], next)} />
        </label>
      ))}
    </div>
  );
}

// ── 按存储形状推出的通用编辑（嵌套结构、列表与没有作者字段的成员） ──

const integerTypes = new Set(['System.Int32', 'System.Int64', 'System.Int16', 'System.Byte', 'System.UInt32', 'System.UInt64', 'System.UInt16', 'System.SByte']);
const preciseIntegerTypes = new Set(['System.Int64', 'System.UInt64']);
const floatTypes = new Set(['System.Single', 'System.Double', 'System.Decimal']);

export function defaultFor(shape: StorageShape | null | undefined): JsonValue {
  if (!shape) return null;
  if (shape.defaultValue !== undefined) return cloneJson(shape.defaultValue);
  if (shape.clrType === 'BTSMTL.Orchestration.ObjectHandle') return '0';
  if (shape.enumValues?.length) return shape.enumValues[0];
  if (shape.clrType === 'System.Boolean') return false;
  if (floatTypes.has(shape.clrType)) return new JsonFloat(0);
  if (integerTypes.has(shape.clrType)) return 0;
  if (shape.clrType === 'System.String') return '';
  if (shape.element) return [];
  if (shape.members) return Object.fromEntries(shape.members.map(member => [member.member, defaultFor(member)]));
  return null;
}

function ValueEditor({ label, value, shape, keys, graphId = null, scope = '', onSet }: {
  label: string; value: JsonValue | undefined; shape: StorageShape | null | undefined; keys: JsonKey[];
  graphId?: string | null; scope?: string; onSet: SetValue;
}): ReactNode {
  const set = (next: JsonValue) => { if (!sameValue(value, next)) onSet(keys, next); };
  if (shape?.clrType === 'BTSMTL.Orchestration.ObjectHandle')
    return <Row label={label} tip="由运行时创建或查询对象后写入"><span className={styles.readOnly}>{valueText(value)}</span></Row>;
  if (isCurve(value, shape?.clrType))
    return <CurveEditor label={label} value={value ?? null} onSet={next => onSet(keys, next)} />;
  if (shape?.enumValues?.length)
    return <Row label={label}><Select label={label} value={typeof value === 'string' ? value : null}
      options={shape.enumValues.map(name => ({ value: name, label: name }))} onChange={next => set(next)} /></Row>;
  if (typeof value === 'boolean' || (value == null && shape?.clrType === 'System.Boolean'))
    return <Row label={label}><Checkbox label={label} checked={value === true} onChange={next => set(next)} /></Row>;
  if (typeof value === 'bigint' || preciseIntegerTypes.has(shape?.clrType ?? ''))
    return <Row label={label}><IntegerValue label={label} value={value} onSet={set} /></Row>;
  if (value instanceof JsonFloat || (typeof value === 'number' && !Number.isInteger(value)) || (value == null && floatTypes.has(shape?.clrType ?? '')))
    return <Row label={label}><NumberValue label={label} value={value} integer={false} onSet={set} /></Row>;
  if (typeof value === 'number' || (value == null && integerTypes.has(shape?.clrType ?? '')))
    return <Row label={label}><NumberValue label={label} value={value} integer onSet={set} /></Row>;
  if (typeof value === 'string' || (value == null && shape?.clrType === 'System.String'))
    return <Row label={label}><TextValue label={label} value={value ?? null} onSet={set} /></Row>;
  if (Array.isArray(value))
    return <ListEditor label={label} value={value} shape={shape?.element} keys={keys} graphId={graphId} scope={scope} onSet={onSet} />;
  if (shape?.hasAuthoringFields) {
    if (value == null) {
      const created = shape.defaultValue;
      return <Row label={label}>{created === undefined
        ? <span className={styles.issue}>该类型没有正式的创建默认值</span>
        : <Button variant="ghost" onClick={() => onSet(keys, cloneJson(created))}>添加 {label}</Button>}</Row>;
    }
    return <AuthoringValueFields label={label} type={shape.clrType} value={value as JsonObject} graphId={graphId}
      identity={JSON.stringify([scope, keys])} onSet={next => onSet(keys, next)} />;
  }
  if (value && typeof value === 'object')
    return <StructEditor label={label} value={value as JsonObject} shape={shape} keys={keys} graphId={graphId} scope={scope} onSet={onSet} />;
  // 空值：有存储形状时可按默认值创建；没有形状的按身份字符串填写。
  const created = defaultFor(shape);
  return created !== null
    ? <Row label={label}><Button variant="ghost" onClick={() => onSet(keys, created)}>创建</Button></Row>
    : <Row label={label}><TextValue label={label} value={null} nullable onSet={set} /></Row>;
}

function StructEditor({ label, value, shape, keys, graphId, scope, onSet }: {
  label: string; value: JsonObject; shape: StorageShape | null | undefined; keys: JsonKey[];
  graphId: string | null; scope: string; onSet: SetValue;
}) {
  const members = shape?.members ?? [];
  const names = [...members.map(member => member.member), ...Object.keys(value).filter(key => !members.some(member => member.member === key))];
  const axes = ['x', 'y', 'z', 'w'].slice(0, names.length);
  if (names.length >= 2 && names.length <= 4 && names.every((name, index) => name === axes[index]))
    return <Row label={label}><VectorValue label={label} value={value} axes={axes} onSet={(sub, next) => onSet([...keys, ...sub], next)} /></Row>;
  return (
    <div className={styles.group}>
      <div className={styles.groupHead}>{label}</div>
      {names.map(name => (
        <ValueEditor key={name} label={memberLabel(name)} value={value[name]} shape={members.find(member => member.member === name)}
          keys={[...keys, name]} graphId={graphId} scope={scope} onSet={onSet} />
      ))}
    </div>
  );
}

function ListEditor({ label, value, shape, keys, graphId, scope, onSet }: {
  label: string; value: JsonValue[]; shape: StorageShape | null | undefined; keys: JsonKey[];
  graphId: string | null; scope: string; onSet: SetValue;
}) {
  const append = (created: JsonValue) => onSet(keys, [...value.map(item => cloneJson(item)), created]);
  const created = shape?.defaultValue;
  const add = shape?.hasAuthoringFields
    ? created === undefined ? null : () => append(cloneJson(created))
    : () => append(value.length ? cloneJson(value[value.length - 1]) : defaultFor(shape));
  const remove = (index: number) => onSet(keys, value.filter((_, item) => item !== index).map(item => cloneJson(item)));
  return (
    <div className={styles.group}>
      <div className={styles.groupHead}>
        {label}<span className={styles.count}>{value.length}</span>
        <Button variant="ghost" className={styles.iconButton} disabled={add === null}
          tip={add === null ? '该类型没有正式的创建默认值' : '添加一项'} aria-label="添加一项" onClick={add ?? undefined}><AddIcon /></Button>
      </div>
      {value.map((item, index) => (
        <div key={index} className={styles.listItem}>
          <ValueEditor label={`[${index}]`} value={item} shape={shape} keys={[...keys, index]} graphId={graphId} scope={scope} onSet={onSet} />
          <Button variant="ghost" className={styles.iconButton} tip="删除这一项" aria-label="删除这一项" onClick={() => remove(index)}><CloseIcon /></Button>
        </div>
      ))}
    </div>
  );
}

// 每项是一个执行出口的列表（组合步骤，身份 m_Id 即端口）：节点上只改名称、顺序与增删；
// 条件与调度在该出口的连线上编辑。连着线的出口要先断线才能删。
function PortListEditor({ label, value, shape, keys, node, outputs, onSet }: {
  label: string; value: JsonObject[]; shape: StorageShape | null | undefined; keys: JsonKey[]; node: JsonObject;
  outputs: DescribedPort[]; onSet: SetValue;
}) {
  const connected = new Set((node.outConnections as JsonObject[]).map(connection => connection.sourcePortID as string));
  const write = (next: JsonObject[]) => onSet(keys, next.map(item => cloneJson(item)));
  const move = (from: number, to: number) => {
    const next = [...value];
    [next[from], next[to]] = [next[to], next[from]];
    write(next);
  };
  const add = () => {
    const created: JsonObject = { ...(defaultFor(shape?.element) as JsonObject), m_Condition: null, m_Id: crypto.randomUUID().replaceAll('-', '') };
    write([...value, Object.fromEntries(Object.keys(created).sort().map(key => [key, created[key]]))]);
  };
  return (
    <div className={styles.group}>
      <div className={styles.groupHead}>
        {label}<span className={styles.count}>{value.length}</span>
        <Button variant="ghost" className={styles.iconButton} tip="添加一个出口" aria-label="添加一个出口" onClick={add}><AddIcon /></Button>
      </div>
      {value.map((item, index) => {
        const id = item.m_Id as string;
        const linked = connected.has(id);
        const port = outputs.find(entry => entry.port.ID === id)?.port.name ?? `${index + 1}.`;
        return (
          <div key={id} className={styles.portItem}>
            <span className={styles.portIndex}>{port.slice(0, port.indexOf('.') + 1) || `${index + 1}.`}</span>
            <TextValue label={`${port} 名称`} value={(item.m_Name as string | undefined) ?? ''}
              onSet={next => onSet([...keys, index, 'm_Name'], next)} />
            <Button variant="ghost" className={styles.iconButton} disabled={index === 0} tip="上移" aria-label="上移"
              onClick={() => move(index, index - 1)}><MoveIcon down={false} /></Button>
            <Button variant="ghost" className={styles.iconButton} disabled={index === value.length - 1} tip="下移" aria-label="下移"
              onClick={() => move(index, index + 1)}><MoveIcon down /></Button>
            <Button variant="ghost" className={styles.iconButton} disabled={linked} tip={linked ? '请先断开该出口的连线' : '删除这个出口'}
              aria-label="删除这个出口" onClick={() => write(value.filter((_, other) => other !== index))}><CloseIcon /></Button>
          </div>
        );
      })}
    </div>
  );
}

// 连线所属步骤的调度字段（条件失效时中断、优先级）；条件图由 children 的引用行新建、移除与进入。
// 规格：LowerPriority／Both 只用于选择执行的步骤，其它组合不提供。
export function StepFields({ graphId, step, children }: { graphId: string; step: EdgeStep; children?: ReactNode }) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? graphPath(graphId) : null);
  const node = ((document as JsonObject | null)?.allNodes as JsonObject[] | undefined)?.find(item => item.UID === step.nodeId) ?? null;
  const { description } = useDescription(skill, graphId, node?.type as string | undefined, node ? configurationOf(node) : null);
  const shape = description?.stored.find(item => item.member === step.member)?.shape?.element;
  const selector = step.nodeType === 'BtsmtlSkillSelectorFlowNode';
  const set: SetValue = (keys, value) => graphAuthoring.setNodeValues(graphId, step.nodeId, [{ keys: [step.member, step.index, ...keys], value }]);
  return (
    <Fold title="步骤" defaultOpen>
      {children}
      {(shape?.members ?? []).filter(member => !['m_Id', 'm_Name', 'm_Condition'].includes(member.member)).map(member => (
        <ValueEditor key={member.member} label={memberLabel(member.member)} value={step.step[member.member]}
          shape={member.member === 'm_AbortPolicy' && !selector
            ? { ...member, enumValues: (member.enumValues ?? []).filter(name => name === 'None' || name === 'Self') } : member}
          keys={[member.member]} onSet={set} />
      ))}
      {!shape && <div className={styles.note}>读取字段</div>}
    </Fold>
  );
}

// ── 节点字段 ──

interface Description {
  stored: StoredField[];
  computed: ComputedField[];
  ports: DescribedPort[];
}

interface DescribeIssue {
  path: string;
  message: string;
}

// 节点身份、类型与连线不属于配置；节点名是图上的显示名，不交给类型描述。
const configurationOf = (node: JsonObject): JsonObject =>
  Object.fromEntries(Object.entries(node).filter(([key]) => !['UID', 'type', 'outConnections', '_name'].includes(key)));

function useDescription(skill: AuthoringTarget | null, graphId: string | null, type: string | undefined, configuration: JsonObject | null) {
  const version = useAuthoring(state => authoringDocument(state).version);
  const carrierRevision = useAuthoring(state => state.carrierRevision);
  const scene = useWorkbench(state => selectedScene(state));
  const [description, setDescription] = useState<Description | null>(null);
  const [issues, setIssues] = useState<DescribeIssue[]>([]);
  // 节点对象被原地编辑，按配置文本判断是否需要重新描述。
  const key = configuration ? stringifySkillJson(configuration, '\n') : null;
  const latest = useRef(configuration);
  latest.current = configuration;
  useEffect(() => {
    setDescription(null);
    setIssues([]);
    if (!skill || !type || key === null) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      authoringApi.describeConfiguration(skill, type, latest.current!, graphId, authoringActions.editingView(), controller.signal).then(result => {
        if (controller.signal.aborted) return;
        const items = result.fields.items;
        setDescription({
          stored: items.filter((item): item is StoredField => !item.computed),
          computed: items.filter((item): item is ComputedField => item.computed === true),
          ports: result.ports.items ?? [],
        });
        setIssues([]);
      }, error => {
        if (controller.signal.aborted || isAbort(error)) return;
        if (error instanceof ApiError && error.status === 422) {
          const body = JSON.parse(error.raw) as { errors?: DescribeIssue[]; message?: string };
          setIssues(body.errors?.length ? body.errors : [{ path: '', message: body.message ?? error.reason }]);
        } else setIssues([{ path: '', message: error instanceof Error ? error.message : String(error) }]);
      });
    }, 150);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [skill, type, key, graphId, version, carrierRevision, scene]);
  return { description, issues };
}

const visible = (field: FieldMetadata, values: Map<string, JsonValue | undefined>) =>
  field.authoringVisible && (!field.visibleWhenField ||
    String(values.get(field.visibleWhenField) ?? '').toLowerCase() === (field.visibleWhenValue ?? '').toLowerCase());

// 作者字段对应的存储形状：一层成员路径取 item 自身形状，更深的沿 members 找。
function shapeAt(item: StoredField, path: string[]): StorageShape | null {
  let shape: StorageShape | null = item.shape;
  for (const name of path.slice(1)) shape = /^\d+$/.test(name)
    ? shape?.element ?? null : shape?.members?.find(member => member.member === name) ?? null;
  return shape;
}

function descriptionValues(description: Pick<Description, 'stored' | 'computed'> | null, value: JsonObject) {
  const values = new Map<string, JsonValue | undefined>();
  for (const item of description?.stored ?? [])
    for (const field of item.fields) values.set(field.fieldId, getIn(value, field.serializedMemberPath));
  for (const field of description?.computed ?? []) values.set(field.fieldId, field.value);
  return values;
}

function DescriptionStatus({ description, issues }: { description: Description | null; issues: DescribeIssue[] }) {
  return <>
    {issues.map((issue, index) => {
      const text = `${issue.path ? `${issue.path}：` : ''}${issue.message}`;
      return <Tip key={index} name={text}><div className={styles.issue} data-name>{text}</div></Tip>;
    })}
    {!description && !issues.length && <div className={styles.note}>读取字段</div>}
  </>;
}

function ComputedFields({ fields }: { fields: ComputedField[] }) {
  return fields.length > 0 && <Fold title="推导">
    {fields.map(field => <Row key={field.fieldId} label={field.displayName ?? field.fieldId} tip="由配置推出，只读">
      <span className={styles.readOnly}>{valueText(field.value)}</span>
    </Row>)}
  </Fold>;
}

function AuthoringValueFields({ label, type, value, graphId, identity, onSet }: {
  label: string; type: string; value: JsonObject; graphId: string | null; identity: string; onSet(value: JsonObject): void;
}) {
  const skill = useAuthoring(state => state.target);
  const { description, issues } = useDescription(skill, graphId, type, value);
  const owner: ReferenceOwner = { kind: 'value', graphId, identity: JSON.stringify([skill?.kind, skill?.name, identity]) };
  return <Fold title={label} defaultOpen>
    <DescriptionStatus description={description} issues={issues} />
    <ValueFields value={value} description={description} owner={owner} onSet={onSet} />
  </Fold>;
}

export function MetadataConfigurationFields({ description, identity, value, onSet }: {
  description: TypeDescription; identity: string; value: JsonObject; onSet(value: JsonObject): void;
}) {
  const fields = { stored: description.fields.items.filter((item): item is StoredField => !item.computed),
    computed: description.fields.items.filter((item): item is ComputedField => item.computed === true) };
  return description.availability.available
    ? <ValueFields value={value} description={fields} owner={{ kind: 'value', graphId: null, identity }} onSet={onSet} />
    : <div className={styles.issue}>{description.availability.reason}</div>;
}

function ValueFields({ value, description, owner, onSet }: {
  value: JsonObject; description: Pick<Description, 'stored' | 'computed'> | null; owner: ReferenceOwner; onSet(value: JsonObject): void;
}) {
  const values = descriptionValues(description, value);
  const setFields: SetFields = changes => {
    const next = cloneJson(value);
    for (const change of changes) setIn(next, change.keys, change.value);
    onSet(next);
  };
  const set: SetValue = (keys, next) => setFields([{ keys, value: next }]);
  return <>
    {description?.stored.map(item => <StoredItem key={item.member} item={item} node={value} values={values} owner={owner}
      onSet={set} onSetFields={setFields} />)}
    <ComputedFields fields={description?.computed.filter(field => visible(field, values)) ?? []} />
  </>;
}

export function SkillFields({ editable }: { editable: boolean }) {
  const target = useAuthoring(state => state.target);
  const execution = useAuthoring(state => authoringDocument(state).execution);
  const { document } = useCurrentDocument(target?.kind === 'skill' ? 'skill.json' : null);
  if (target?.kind !== 'skill' || document === null) return null;
  return <fieldset className={styles.authorFields} disabled={!editable}>
    <Fold title="执行 Scope" defaultOpen><Facts fields={[
      { displayName: '预览 Scope', value: execution.previewScope ?? '未显式选择' },
      { displayName: '实际运行 Scope', value: execution.runtimeScope ?? '未显式选择' },
    ]} /><Button onClick={() => openPanel('resources')}>从正式资源目录选择 Scope</Button>
      <div className={styles.note}>保存与导出按所选 Scope 和实际授予关系准备内容；无法唯一确定时会说明缺项。</div>
    </Fold>
    <AuthoringValueFields label="技能配置" type="ThirdPersonCharacter.ActionSystem.GameplayAbilityDefinition" value={document as JsonObject}
      graphId={null} identity="skill.json" onSet={next => authoringActions.edit('skill.json', value => { Object.assign(value as JsonObject, next); })} />
  </fieldset>;
}

// 节点一栏：名称与按合同描述的字段；children 为同栏的引用行（状态体、子图、转移等）。
export function NodeFields({ graphId, nodeId, children }: { graphId: string; nodeId: string; children?: ReactNode }) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? graphPath(graphId) : null);
  const node = ((document as JsonObject | null)?.allNodes as JsonObject[] | undefined)?.find(item => item.UID === nodeId) ?? null;
  const { description, issues } = useDescription(skill, graphId, node?.type as string | undefined, node ? configurationOf(node) : null);
  if (!node) return null;
  const setFields: SetFields = changes => graphAuthoring.setNodeValues(graphId, nodeId, changes);
  const set: SetValue = (keys, value) => setFields([{ keys, value }]);
  const values = descriptionValues(description, node);
  const computed = description?.computed.filter(field => visible(field, values)) ?? [];
  const connected = new Set(((document as JsonObject).allNodes as JsonObject[]).flatMap(item =>
    (item.outConnections as JsonObject[]).filter(connection => connection.targetNode === nodeId).map(connection => connection.targetPortID as string)));
  const inputs = (description?.ports ?? []).filter(port => port.direction === 'input' && port.port.type !== 'Flow' && !connected.has(port.port.ID));
  const outputs = (description?.ports ?? []).filter(port => port.direction === 'output');
  return (
    <>
      <Fold title="节点" defaultOpen>
        <Row label="名称"><TextValue label="名称" value={(node._name as string | undefined) ?? ''} onSet={next => set(['_name'], next)} /></Row>
        <DescriptionStatus description={description} issues={issues} />
        {description?.stored.filter(item => item.member !== '_inputPortValues' && !(item.member === 'm_Body' && isState(node))).map(item => (
          <StoredItem key={item.member} item={{ ...item, value: node[item.member] ?? null }} node={node} values={values}
            owner={{ kind: 'node', graphId, nodeId }} outputs={outputs} onSet={set} onSetFields={setFields} />
        ))}
        {children}
      </Fold>
      {inputs.length > 0 && (
        <Fold title="端口默认值" defaultOpen>
          {inputs.map(port => {
            const keys = ['_inputPortValues', port.port.ID];
            const current = getIn(node, keys) ?? (port.port.serializedValue as JsonValue);
            return <ValueEditor key={port.port.ID} label={port.port.name} value={current} shape={portShape(port.port.type)}
              keys={keys} graphId={graphId} scope={`${graphId}/${nodeId}/${port.port.ID}`} onSet={set} />;
          })}
        </Fold>
      )}
      <ComputedFields fields={computed} />
    </>
  );
}

// 输入片段与输入标记共用：两种图都在 @timelineEnable 锚点下写按键。
export function InputClipFields({ documentId, graphId }: { documentId: string; graphId: string }) {
  const elements = useSkillElements(true);
  const context = useInputContext(documentId);
  const { document } = useCurrentDocument(graphPath(graphId));
  if (!elements || !document) return <div className={styles.note}>正在读取输入字段</div>;
  if (context.kind === 'unavailable') return <div className={styles.note}>{context.reason}</div>;
  const graph = document as JsonObject;
  const nodes = graph.allNodes as JsonObject[];
  const anchor = elements.anchors[graph.Role as string]?.find(item => item.kind === '@timelineEnable');
  const entry = nodes.find(node => node.UID === anchor?.uid);
  const writers = ((entry?.outConnections as JsonObject[] | undefined) ?? [])
    .map(edge => nodes.find(node => node.UID === edge.targetNode)!)
    .filter(node => node.type === elements.previewInput.nodeType);
  return <>
    {writers.map(node => <InputWriterFields key={node.UID as string} graphId={graphId} node={node}
      contract={elements.previewInput} keys={context.actor.keys} />)}
  </>;
}

function InputWriterFields({ graphId, node, contract, keys }: {
  graphId: string; node: JsonObject; contract: import('../../api/authoring').SkillElements['previewInput'];
  keys: import('../../api/types').InputCatalog['players'][number]['keys'];
}) {
  const target = useAuthoring(state => state.target);
  const { description, issues } = useDescription(target, graphId, node.type as string, configurationOf(node));
  const key = getIn(node, contract.keyPath);
  const kind = keys.find(item => item.name === key)?.kind;
  const fields = description?.stored.map(item => ({ ...item, fields: item.fields
    .filter(field => field.serializedMemberPath.join('/') === contract.keyPath.join('/') ||
      (kind !== undefined && field.fieldId === contract.valueFields[kind]))
    .map((field): FieldMetadata => field.serializedMemberPath.join('/') === contract.keyPath.join('/')
      ? { ...field, choiceKind: 'preview-input-key', choicesAvailability: { available: true },
        choices: keys.map(item => ({ value: item.name, name: item.name, detail: item.kind, source: '当前载体输入', conflict: null, targets: [], associated: [] })) } : field),
  })).filter(item => item.fields.length) ?? [];
  const setFields: SetFields = changes => inputAuthoring.setNodeValues(graphId, node.UID as string, changes, contract);
  const set: SetValue = (path, value) => setFields([{ keys: path, value }]);
  return <Fold title="输入" defaultOpen>
    {issues.map((issue, index) => <div key={index} className={styles.issue}>{issue.message}</div>)}
    {fields.map(item => <StoredItem key={item.member} item={item} node={node} values={new Map()}
      owner={{ kind: 'node', graphId, nodeId: String(node.UID) }} onSet={set} onSetFields={setFields} />)}
  </Fold>;
}

// 状态机的状态：状态体由检查器的“状态体”一栏新建、移除与进入，不按身份字符串编辑。
export const isState = (node: JsonObject) => typeof node.type === 'string' && /^BtsmtlSkillNative\w*State$/.test(node.type);

// ── 转移：Payload 按原 C# 存储形状编辑；条件图由检查器单独新建与移除 ──

export function TransitionFields({ graphId, connectionId, shape, children }: {
  graphId: string; connectionId: string; shape: StorageShape; children?: ReactNode;
}) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? graphPath(graphId) : null);
  const connection = (((document as JsonObject | null)?.allNodes as JsonObject[] | undefined) ?? [])
    .flatMap(node => node.outConnections as JsonObject[]).find(item => item.UID === connectionId) ?? null;
  if (!connection) return null;
  const payload = connection.Payload as JsonObject;
  const set: SetValue = (keys, value) => graphAuthoring.setTransitionValue(graphId, connectionId, keys, value);
  return (
    <Fold title="转移" defaultOpen>
      {(shape.members ?? []).filter(member => member.member !== 'm_Condition').map(member => (
        <ValueEditor key={member.member} label={memberLabel(member.member)} value={payload[member.member]} shape={member}
          keys={[member.member]} onSet={set} />
      ))}
      {children}
    </Fold>
  );
}

// ── 黑板：每个变量一组，原生变量（名称、类型、默认值）与 BTSMTL 声明（作用域、生命周期、分类、输入投影）一起编辑 ──

export function BlackboardFields({ graphId, shape, editable }: { graphId: string; shape: StorageShape | null; editable: boolean }) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? graphPath(graphId) : null);
  const [draft, setDraft] = useState({ name: '', varType: 'Single' });
  const graph = document as JsonObject | null;
  if (!graph || !Array.isArray(graph.BlackboardDeclarations)) return <div className={styles.note}>这张图没有黑板</div>;
  const variables = Object.values(((graph.localBlackboard as JsonObject | undefined)?.variables ?? {}) as JsonObject) as JsonObject[];
  const declarations = graph.BlackboardDeclarations as JsonObject[];
  const members = shape?.members ?? [];
  const add = () => {
    const name = draft.name.trim();
    if (!name) return;
    // 新变量默认动作实例作用域与生命周期（合法配对），在声明里改。
    graphAuthoring.addVariable(graphId, name, draft.varType, { m_Scope: 'ActionInstance', m_Lifetime: 'ActionInstance', m_Category: '' });
    setDraft({ ...draft, name: '' });
  };
  return (
    <>
      {variables.map(variable => {
        const id = variable.ID as string;
        const declaration = declarations.find(item => item.m_VariableId === id);
        const setDeclaration: SetValue = (keys, value) => graphAuthoring.setDeclarationValue(graphId, id, keys, value);
        return (
          <div key={id} className={styles.group}>
            <div className={styles.groupHead}>
              {variable.name as string}<span className={styles.count}>{variable.varType as string}</span>
              {editable && <Button variant="ghost" className={styles.iconButton} tip="删除变量与声明；引用它的节点保存时由编译报告" aria-label="删除变量"
                onClick={() => graphAuthoring.removeVariable(graphId, id)}><CloseIcon /></Button>}
            </div>
            {editable ? (
              <>
                <Row label="名称"><TextValue label="名称" value={variable.name as string} onSet={next => {
                  if (typeof next === 'string' && next.trim()) graphAuthoring.renameVariable(graphId, id, next.trim());
                }} /></Row>
                <ValueEditor label="默认值" value={variable.serializedValue}
                  shape={portShape(variable.varType as string)} keys={[]}
                  onSet={(keys, value) => graphAuthoring.setVariableValue(graphId, id, keys, value)} />
                {declaration ? members.filter(item => item.member !== 'm_VariableId').map(item => (
                  <ValueEditor key={item.member} label={memberLabel(item.member)} value={declaration[item.member]} shape={item}
                    keys={[item.member]} onSet={setDeclaration} />
                )) : <div className={styles.issue}>缺少 BTSMTL 声明</div>}
              </>
            ) : (
              <>
                <Row label="默认值"><span className={styles.readOnly}>{stringifySkillJson(variable.serializedValue ?? null, ' ')}</span></Row>
                {declaration && ['m_Scope', 'm_Lifetime', 'm_Category'].map(key => (
                  <Row key={key} label={memberLabel(key)}><span className={styles.readOnly}>{String(declaration[key] ?? '')}</span></Row>
                ))}
              </>
            )}
          </div>
        );
      })}
      {!variables.length && <div className={styles.note}>没有变量</div>}
      {editable && (
        <div className={styles.group}>
          <div className={styles.groupHead}>添加变量</div>
          <Row label="名称"><TextInput aria-label="变量名称" value={draft.name} onChange={event => setDraft({ ...draft, name: event.currentTarget.value })}
            onKeyDown={event => { if (event.key === 'Enter') add(); }} /></Row>
          <Row label="类型"><Select label="类型" value={draft.varType} options={variableTypes.map(type => ({ value: type, label: type }))}
            onChange={varType => setDraft({ ...draft, varType })} /></Row>
          <Row label=""><Button variant="ghost" disabled={!draft.name.trim()} onClick={add}>添加</Button></Row>
        </div>
      )}
    </>
  );
}

// ── 预览输入方案：方案列表（所选方案、新增、删除）与所选方案的字段 ──

export function InputPlanFields({ path, editable }: { path: string; editable: boolean }) {
  const { document } = useCurrentDocument(path);
  const plan = document as JsonObject | null;
  if (!plan) return null;
  const schemes = plan.Schemes as JsonObject[];
  const selected = numberOf(plan.Selected);
  const scheme = schemes[selected];
  const set: SetValue = (keys, value) => inputAuthoring.setValue(path, keys, value);
  return (
    <>
      <Row label="方案">
        <Select label="方案" value={String(selected)} disabled={!editable}
          options={schemes.map((item, index) => ({ value: String(index), label: (item.Name as string) || `方案 ${index + 1}` }))}
          onChange={value => set(['Selected'], Number(value))} />
      </Row>
      {editable && (
        <Row label="">
          <Button variant="ghost" tip="复制所选方案的设置，配一条新的空输入 Timeline" onClick={() => {
            void inputAuthoring.addScheme(path, `方案 ${schemes.length + 1}`).catch(error => consoleActions.error('输入', error));
          }}>新增方案</Button>
          <Button variant="ghost" disabled={schemes.length < 2} tip={schemes.length < 2 ? '至少保留一个方案' : '删除所选方案；它专属的输入 Timeline 保存时删除'}
            onClick={() => inputAuthoring.removeScheme(path, selected)}>删除方案</Button>
        </Row>
      )}
      {scheme && Object.keys(scheme).filter(key => key !== 'InputTimeline').map(key => editable
        ? <ValueEditor key={key} label={memberLabel(key)} value={scheme[key]} shape={null} keys={['Schemes', selected, key]} onSet={set} />
        : <Row key={key} label={memberLabel(key)}><span className={styles.readOnly}>{stringifySkillJson(scheme[key] ?? null, ' ')}</span></Row>)}
    </>
  );
}

// ── 轨道字段：与片段相同按 JSON 值编辑；身份、合同与所含片段、标记不在此编辑 ──

const trackFixed = new Set(['m_AuthoringId', 'ContractKind', 'm_Clips', 'm_Markers']);

// 轨道与片段按原 C# 类型的存储形状编辑（枚举给下拉、布尔给勾选）；形状取宿主 elements 中同合同种类的模板。
function useElementShape(kind: 'tracks' | 'clips', contract: JsonValue | undefined): StorageShape | null {
  const elements = useSkillElements(true);
  return elements?.[kind].find(item => item.kind === contract)?.shape ?? null;
}

const memberShape = (shape: StorageShape | null, member: string) => shape?.members?.find(item => item.member === member) ?? null;

export function TrackFields({ documentId, trackId, children }: { documentId: string; trackId: string; children?: ReactNode }) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? timelinePath(documentId) : null);
  const track = (((document as JsonObject | null)?.m_Tracks as JsonObject[] | undefined) ?? []).find(item => item.m_AuthoringId === trackId) ?? null;
  const shape = useElementShape('tracks', track?.ContractKind);
  if (!track) return null;
  const set: SetValue = (keys, value) => authoringActions.setTrackValue(documentId, trackId, keys, value);
  return (
    <Fold title="轨道" defaultOpen>
      {Object.keys(track).filter(key => !trackFixed.has(key)).map(key => (
        <ValueEditor key={key} label={memberLabel(key)} value={track[key]} shape={memberShape(shape, key)} keys={[key]} onSet={set} />
      ))}
      {children}
    </Fold>
  );
}

// 端口值类型名（C# 短名）对应的存储形状。
function portShape(type: string): StorageShape {
  const clrType = { Single: 'System.Single', Double: 'System.Double', Int32: 'System.Int32', Int64: 'System.Int64',
    UInt64: 'System.UInt64', Boolean: 'System.Boolean', String: 'System.String',
    ObjectHandle: 'BTSMTL.Orchestration.ObjectHandle', 'object-handle': 'BTSMTL.Orchestration.ObjectHandle' }[type] ?? type;
  return { clrType, hasAuthoringFields: false, enumValues: null, elementType: null };
}

function StoredItem({ item, node, values, owner, outputs = [], onSet, onSetFields }: {
  item: StoredField; node: JsonObject; values: Map<string, JsonValue | undefined>;
  owner: ReferenceOwner; outputs?: DescribedPort[]; onSet: SetValue; onSetFields: SetFields;
}) {
  if (!item.fields.length)
    return null;
  return (
    <>
      {rootFields(item.fields).filter(field => visible(field, values)).map(field => (
        <FieldEditor key={field.serializedMemberPath.join('/')} id={`${owner.kind === 'node' ? `${owner.graphId}/${owner.nodeId}` : owner.identity}/${field.serializedMemberPath.join('/')}`} field={field} item={item} node={node} owner={owner}
          values={values} outputs={outputs} onSet={onSet} onSetFields={onSetFields} />
      ))}
    </>
  );
}

function CreateOwnedGraph({ owner, path, target }: {
  owner: Extract<ReferenceOwner, { kind: 'node' }>; path: string[]; target: Extract<GraphReferenceTarget, { ownerBinding: 'ReferenceNode' }>;
}) {
  const [creating, setCreating] = useState(false);
  return <Button disabled={creating} onClick={() => {
    setCreating(true);
    graphAuthoring.createOwnedReference({ ...owner, path, target })
      .catch((error: unknown) => consoleActions.error('作者', error))
      .finally(() => setCreating(false));
  }}>{creating ? '正在创建' : `新建 ${target.role}`}</Button>;
}

const belowPath = (path: string[], parent: string[]) => path.length > parent.length &&
  parent.every((member, index) => path[index] === member);

const rootFields = (fields: FieldMetadata[]) => fields.filter(field =>
  !fields.some(parent => belowPath(field.serializedMemberPath, parent.serializedMemberPath)));

function FieldReferencePicker({ id, label, field, value, configuration, onChoose }: {
  id: string; label: string; field: FieldMetadata; value: JsonValue | undefined; configuration: JsonObject; onChoose(choice: ReferenceChoice): void;
}) {
  if (field.resourceReference !== null) return <ResourceFieldPicker id={id} label={label} metadata={field.resourceReference}
    value={value} configuration={configuration} onChoose={onChoose} />;
  if (field.objectSelection !== null) {
    const metadata = field.objectSelection;
    return <ReferencePicker id={id} label={label} value={value} configuration={configuration}
      catalog={{ kind: 'unavailable', reason: metadata.kind === 'available' ? '选择文件后读取实际对象候选。' : metadata.reason }}
      onChoose={onChoose} resourceObjects={metadata.kind === 'unavailable' ? metadata : { ...metadata, choose: candidate => {
        onChoose({ value: resourceObjectValue(candidate, metadata.valueMember), name: candidate.name, detail: candidate.type,
          source: candidate.value, conflict: null, targets: [], associated: [] });
      } }} />;
  }
  const catalog: ReferenceCatalog = field.choiceKind === null
      ? { kind: 'unavailable', reason: '该字段尚未提供正式候选目录' }
      : field.choicesAvailability.available
        ? { kind: 'ready', referenceKind: field.choiceKind, items: field.choices,
          invalidReason: '引用已失效，当前值不在正式候选中，原值已保留' }
        : { kind: 'unavailable', reason: field.choicesAvailability.reason };
  return <ReferencePicker id={id} label={label} value={value}
    configuration={configuration} catalog={catalog} onChoose={onChoose} />;
}

function writeReferenceChoice(choice: ReferenceChoice, keys: JsonKey[], value: JsonValue, onSetFields: SetFields) {
  onSetFields([
    { keys, value },
    ...choice.associated.map(associated => ({ keys: associated.serializedMemberPath, value: associated.value })),
  ]);
}

function ReferenceCollection({ id, label, field, value, configuration, onSet, onSetFields }: {
  id: string; label: string; field: FieldMetadata; value: JsonValue[]; configuration: JsonObject; onSet: SetValue; onSetFields: SetFields;
}) {
  const path = field.serializedMemberPath;
  return <div className={styles.group}>
    <div className={styles.groupHead}>{label}<span className={styles.count}>{value.length}</span></div>
    {value.map((element, index) => <div key={index} className={styles.listItem}>
      <FieldReferencePicker id={`${id}/${index}`} label={`${label} [${index}]`} field={field} value={element} configuration={configuration}
        onChoose={choice => writeReferenceChoice(choice, [...path, index], choice.value, onSetFields)} />
      <Button variant="ghost" className={styles.iconButton} tip="删除这一项" aria-label="删除这一项"
        onClick={() => onSet(path, value.filter((_, item) => item !== index).map(item => cloneJson(item)))}><CloseIcon /></Button>
    </div>)}
    <Row label="添加一项"><FieldReferencePicker id={`${id}/add`} label={`添加${label}`} field={field} value={undefined} configuration={configuration}
      onChoose={choice => writeReferenceChoice(choice, path, [...value.map(item => cloneJson(item)), choice.value], onSetFields)} /></Row>
  </div>;
}

function PolymorphicValue({ label, field, value, children, onSet }: {
  label: string; field: FieldMetadata; value: JsonValue | undefined; children: ReactNode; onSet(value: JsonValue): void;
}) {
  const selected = field.subtypes.find(subtype => subtype.identity === field.selectedSubtype);
  return <fieldset className={styles.authorFields} disabled={field.readOnly}>
    <Row label={label}><Select label={`${label} 类型`} value={field.selectedSubtype}
      options={field.subtypes.map(subtype => ({ value: subtype.identity, label: subtype.identity }))}
      placeholder="选择类型" onChange={identity => {
        const index = field.subtypes.findIndex(subtype => subtype.identity === identity);
        onSet(cloneJson(field.subtypes[index].defaultValue));
      }} /></Row>
    {selected && <div className={styles.note}>{selected.comment}</div>}
    {selected && value !== null && children}
    {field.optional && value !== null && <Row label="">
      <Button variant="ghost" onClick={() => onSet(null)}>移除 {label}</Button>
    </Row>}
  </fieldset>;
}

function FieldEditor({ id, field, item, node, owner, values, outputs, onSet, onSetFields }: {
  id: string; field: FieldMetadata; item: StoredField; node: JsonObject; owner: ReferenceOwner;
  values: Map<string, JsonValue | undefined>; outputs: DescribedPort[]; onSet: SetValue; onSetFields: SetFields;
}): ReactNode {
  const label = field.displayName ?? field.fieldId;
  const path = field.serializedMemberPath;
  const value = getIn(node, path);
  const shape = shapeAt(item, path);
  const set = (next: JsonValue) => { if (!sameValue(value, next)) onSet(path, next); };
  const descendants = item.fields.filter(child => belowPath(child.serializedMemberPath, path));
  const childFields = (prefix: string[]) => rootFields(descendants.filter(child => belowPath(child.serializedMemberPath, prefix)))
    .filter(child => visible(child, values))
    .map(child => <FieldEditor key={child.serializedMemberPath.join('/')} id={`${id}/${child.serializedMemberPath.join('/')}`}
      field={child} item={item} node={node} owner={owner} values={values} outputs={outputs} onSet={onSet} onSetFields={onSetFields} />);
  if (field.valueKind === 'Polymorphic')
    return <PolymorphicValue label={label} field={field} value={value} onSet={set}>{childFields(path)}</PolymorphicValue>;
  if (field.readOnly || field.clrType === 'BTSMTL.Orchestration.ObjectHandle')
    return <Row label={label} tip="只读"><span className={styles.readOnly}>{valueText(value)}</span></Row>;
  if (owner.kind === 'node' && Array.isArray(value) && field.flowOutputList)
    return <PortListEditor label={label} value={value as JsonObject[]} shape={shape} keys={path} node={node} outputs={outputs} onSet={onSet} />;
  if (Array.isArray(value) && field.choicesTarget === 'element')
    return <ReferenceCollection id={id} label={label} field={field} value={value} configuration={node} onSet={onSet} onSetFields={onSetFields} />;
  if (Array.isArray(value) && descendants.length !== 0) {
    const created = shape?.element?.defaultValue;
    const add = created === undefined ? undefined : () => set([...value.map(element => cloneJson(element)), cloneJson(created)]);
    return <div className={styles.group}>
      <div className={styles.groupHead}>{label}<span className={styles.count}>{value.length}</span>
        <Button variant="ghost" className={styles.iconButton} aria-label="添加一项" disabled={created === undefined}
          tip={created === undefined ? '该类型没有正式的创建默认值' : '添加一项'}
          onClick={add}><AddIcon /></Button>
      </div>
      {value.map((element, index) => <div key={index} className={styles.listItem}>
        <div className={styles.group}><div className={styles.groupHead}>[{index}]</div>
          {element === null
            ? created === undefined ? <div className={styles.issue}>该类型没有正式的创建默认值</div>
              : <Button variant="ghost" onClick={() => onSet([...path, index], cloneJson(created))}>添加 {label}</Button>
            : childFields([...path, String(index)])}
        </div>
        <Button variant="ghost" className={styles.iconButton} tip="删除这一项" aria-label="删除这一项"
          onClick={() => set(value.filter((_, item) => item !== index).map(element => cloneJson(element)))}><CloseIcon /></Button>
      </div>)}
    </div>;
  }
  if (field.valueKind === 'Object' && value !== null && !Array.isArray(value) && descendants.length !== 0)
    return <div className={styles.group}><div className={styles.groupHead}>{label}</div>{childFields(path)}
      {field.optional && <Row label=""><Button variant="ghost" onClick={() => set(null)}>移除 {label}</Button></Row>}
    </div>;
  const ownedTargets = field.graphTargets.filter(target => target.ownerBinding === 'ReferenceNode');
  if (owner.kind === 'node' && ownedTargets.length !== 0)
    return <Row label={label}>{value == null || value === ''
      ? ownedTargets.map(target => <CreateOwnedGraph key={target.role} owner={owner} path={path} target={target} />)
      : <span className={styles.readOnly}>{valueText(value)}</span>}</Row>;
  if (field.resourceReference !== null || field.objectSelection !== null || field.choiceKind !== null || field.valueKind === 'IdentityReference' || field.valueKind === 'AssetReference' || field.pickerKind !== null)
    return <Row label={label}><FieldReferencePicker id={id} label={label} field={field} value={value} configuration={node}
      onChoose={choice => writeReferenceChoice(choice, path, choice.value, onSetFields)} /></Row>;
  switch (field.valueKind) {
    case 'Boolean':
      return <Row label={label}><Checkbox label={label} checked={value === true} onChange={set} /></Row>;
    case 'Integer':
      return <Row label={label}>{preciseIntegerTypes.has(field.clrType)
        ? <IntegerValue label={label} value={value} onSet={set} />
        : <NumberValue label={label} value={value} integer onSet={set} />}</Row>;
    case 'Float':
      return <Row label={label}><NumberValue label={label} value={value} integer={false} onSet={set} /></Row>;
    case 'Enum': {
      const names = field.allowedValues ?? shape?.enumValues ?? [];
      return <Row label={label}><Select label={label} value={typeof value === 'string' ? value : null}
        options={names.map(name => ({ value: name, label: name }))} onChange={set} /></Row>;
    }
    case 'Vector2':
    case 'Vector3':
      return <Row label={label}><VectorValue label={label} value={value} axes={field.valueKind === 'Vector2' ? ['x', 'y'] : ['x', 'y', 'z']}
        onSet={(axis, next) => onSet([...path, ...axis], next)} /></Row>;
  }
  if ((value == null || typeof value === 'string') && !(value == null && shape?.members?.length) && !isCurve(value, shape?.clrType ?? field.objectType ?? undefined))
    return <Row label={label} tip={field.references.map(reference => reference.errorMessage).join('\n') || undefined}>
      <TextValue label={label} value={value ?? null}
        nullable={shape?.clrType !== 'System.String'} onSet={set} />
    </Row>;
  return <>
    <ValueEditor label={label} value={value} shape={shape ?? (field.objectType ? { clrType: field.objectType, hasAuthoringFields: false, enumValues: null, elementType: null } : null)}
      keys={path} graphId={owner.graphId} scope={id} onSet={onSet} />
    {field.optional && shape?.hasAuthoringFields && value !== null && <Row label="">
      <Button variant="ghost" onClick={() => set(null)}>移除 {label}</Button>
    </Row>}
  </>;
}

// ── 片段字段：没有类型合同，按 JSON 值本身的类型编辑；FP 时间以秒显示 ──

const clipFixed = new Set(['m_AuthoringId', 'ContractKind', 'm_AssetTree', 'm_StartTimeFPRawValue', 'm_EndTimeFPRawValue']);

// 定点时间换成秒后显示到 0.0001 秒，不显示 1/65536 的尾数。
const shownSeconds = (seconds: number) => Math.round(seconds * 10000) / 10000;

export function TimelineScopeFields({ documentId }: { documentId: string }) {
  const { document } = useCurrentDocument(timelinePath(documentId));
  const catalog = useAdapterCatalog();
  useEffect(() => { void adapterCatalogCache.ensure(authoringApi.catalog); }, []);
  const scope = (document as JsonObject | null)?.m_AuthoringScope as string | null | undefined;
  const values = new Map<string, string[]>();
  if (catalog.kind === 'ready') for (const group of catalog.snapshot.domains.filter(group => group.domain === 'execution-scope'))
    for (const item of group.items) values.set(item.value, [...values.get(item.value) ?? [], `${item.display} · ${group.adapter}`]);
  return <Row label="Timeline 作者 Scope"><Select label="Timeline 作者 Scope" value={scope ?? null}
    options={[...values].map(([value, names]) => ({ value, label: names.join(' / ') }))} placeholder="明确选择 Timeline 的执行范围"
    onChange={value => authoringActions.edit(timelinePath(documentId), timeline => { (timeline as JsonObject).m_AuthoringScope = value; })} />
    {catalog.kind === 'failed' && <span className={styles.issue}>{catalog.failure.text}</span>}
    {catalog.kind === 'ready' && scope != null && !values.has(scope) && <span className={styles.issue}>原 Scope 不在正式目录中，原字符串已保留：{scope}</span>}
  </Row>;
}

// 片段一栏：起止与片段的序列化字段；children 为同栏的引用行（节点图）。
export function ClipFields({ documentId, clipId, timeOnly = false, children }: {
  documentId: string; clipId: string; timeOnly?: boolean; children?: ReactNode;
}) {
  const skill = useAuthoring(state => state.target);
  const { document } = useCurrentDocument(skill ? timelinePath(documentId) : null);
  const clip = (((document as JsonObject | null)?.m_Tracks as JsonObject[] | undefined) ?? [])
    .flatMap(track => (track.m_Clips as JsonObject[] | undefined) ?? []).find(item => item.m_AuthoringId === clipId) ?? null;
  const shape = useElementShape('clips', clip?.ContractKind);
  const elements = useSkillElements(skill !== null);
  const template = elements?.clips.find(template => template.kind === clip?.ContractKind);
  const { description, issues } = useDescription(skill, null, template?.clrType, clip);
  if (!clip) return null;
  const setFields: SetFields = changes => authoringActions.setClipValues(documentId, clipId, changes);
  const set: SetValue = (keys, value) => setFields([{ keys, value }]);
  const resourceFields = description?.stored.filter(item => item.fields.some(field => field.resourceReference !== null || field.objectSelection !== null)) ?? [];
  const resourceMembers = new Set(resourceFields.map(item => item.member));
  const values = descriptionValues(description, clip);
  const owner: ReferenceOwner = { kind: 'value', graphId: null, identity: JSON.stringify([skill, documentId, clipId]) };
  const start = fpSeconds(numberOf(clip.m_StartTimeFPRawValue)), end = fpSeconds(numberOf(clip.m_EndTimeFPRawValue));
  return (
    <ResourceScopeContext.Provider value={(document as JsonObject).m_AuthoringScope as string | null ?? null}>
    <Fold title="片段" defaultOpen>
      <TimelineScopeFields documentId={documentId} />
      {children}
      {!timeOnly && <DescriptionStatus description={description} issues={issues} />}
      {!timeOnly && resourceFields.map(item => <StoredItem key={item.member} item={item} node={clip} values={values} owner={owner}
        onSet={set} onSetFields={setFields} />)}
      <Row label="起点（秒）"><NumberInput wide label="起点" value={shownSeconds(start)} min={0}
        onChange={next => { if (next !== null && next < end) authoringActions.setClipTime(documentId, clipId, next, end); }} /></Row>
      <Row label="终点（秒）"><NumberInput wide label="终点" value={shownSeconds(end)}
        onChange={next => { if (next !== null && next > start) authoringActions.setClipTime(documentId, clipId, start, next); }} /></Row>
      {!timeOnly && description !== null && Object.keys(clip).filter(key => !clipFixed.has(key) && !resourceMembers.has(key)).map(key => key.endsWith('FPRawValue') ? (
        <Row key={key} label={`${memberLabel(key.replace(/FPRawValue$/, ''))}（秒）`}>
          <NumberInput wide label={key} value={shownSeconds(fpSeconds(numberOf(clip[key])))} min={0}
            onChange={next => { if (next !== null && fpRaw(next) !== numberOf(clip[key])) set([key], fpRaw(next)); }} />
        </Row>
      ) : <ValueEditor key={key} label={memberLabel(key)} value={clip[key]} shape={memberShape(shape, key)} keys={[key]} onSet={set} />)}
    </Fold>
    </ResourceScopeContext.Provider>
  );
}
