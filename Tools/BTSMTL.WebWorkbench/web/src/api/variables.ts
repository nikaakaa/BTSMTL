import { postText } from './http';
import { array, boolean, nullable, number, object, optional, text, type Decoder } from './playerRecords';
import { readAuthoringAvailability } from './authoringAvailability';
import { readWorkspaceDocumentInfo, readWorkspaceDocumentView, readWorkspaceEditingView, type WorkspaceEditingView } from './workspaceDocuments';
import { parseSkillJson, stringifySkillJson, type JsonValue } from '../state/skillJson';

const parsedJson: Decoder<JsonValue> = (value, path) => {
  if (value === undefined) throw new Error(`${path} 缺少正式 JSON 值`);
  return value as JsonValue;
};
const enumMember = object({ name: text, value: number });
const inputBinding = object({ m_InputValueId: nullable(text), m_Projection: number });
const declaration = object({ m_VariableId: text, m_Scope: number, m_Lifetime: number,
  m_Category: nullable(text), m_InputBinding: nullable(inputBinding) });
const declaredVariable = object({ m_Declaration: declaration, m_Name: text, m_TypeId: text,
  m_DefaultValue: parsedJson, m_IsPublic: boolean, m_DeclarationOrder: number });
// m_ObjectKind 只出现在句柄变量上；缺省时不写出该成员，草稿与正式文档保持一致。
const variable = (value: unknown, path = 'Variable'): ReturnType<typeof declaredVariable> & { m_ObjectKind?: string } => {
  const declared = declaredVariable(value, path);
  const kind = optional(text)((value as Record<string, unknown>).m_ObjectKind, `${path}.m_ObjectKind`);
  return kind === undefined ? declared : { ...declared, m_ObjectKind: kind };
};
export const readVariablesValue = object({ m_ContentDomain: text, m_OwnerId: text, m_Revision: text, m_Variables: array(variable) });
export type VariablesValue = ReturnType<typeof readVariablesValue>;
const choices = object({
  types: array(object({ typeId: text, clrType: text, valueKind: text, defaultValue: parsedJson, enumMembers: nullable(array(enumMember)) })),
  scopes: array(object({ name: text, value: number, defaultLifetime: number, lifetimes: array(number) })),
  lifetimes: array(enumMember),
  // 句柄投影固定所产出句柄的种类。
  inputProjections: array(object({ name: text, value: number, objectKind: nullable(text) })),
  objectKinds: array(object({ id: text, displayName: text, parent: nullable(text) })),
});
const result = { success: boolean, document: readWorkspaceDocumentInfo, value: readVariablesValue, availability: readAuthoringAvailability };
const readQuery = object({ ...result, choices });
const readEdit = (value: unknown) => ({ ...object({ ...result, variableId: text, editingView: readWorkspaceEditingView })(value, 'VariablesEdit'),
  ...readWorkspaceDocumentView(value, 'VariablesEdit') });

export type VariablesQuery = ReturnType<typeof readQuery>;
export type VariablesEdit = ReturnType<typeof readEdit>;
export type VariableEntry = VariablesQuery['value']['m_Variables'][number];
export type VariableType = VariablesQuery['choices']['types'][number];
export type VariableDeclarationChanges = Partial<Omit<VariableEntry['m_Declaration'], 'm_VariableId'>>;
export type VariableOperation =
  | { kind: 'create'; typeId: string; name?: string; objectKind?: string }
  | { kind: 'rename'; variableId: string; name: string }
  | { kind: 'set-default'; variableId: string; value: JsonValue }
  | { kind: 'set-object-kind'; variableId: string; objectKind: string }
  | { kind: 'configure-public'; variableId: string; isPublic: boolean }
  | { kind: 'configure-blackboard'; variableId: string; changes: VariableDeclarationChanges }
  | { kind: 'delete'; variableId: string };

export const variablesApi = {
  query: (ownerId: string, editingView: WorkspaceEditingView, signal?: AbortSignal) =>
    postText('/api/authoring/documents/queries/variables', { ownerId, editingView }, signal)
      .then(body => readQuery(parseSkillJson(body), 'Variables')),
  edit: (ownerId: string, editingView: WorkspaceEditingView, operation: VariableOperation) =>
    postText('/api/authoring/documents/variables/edit', stringifySkillJson({ ownerId, editingView, operation }, '\n'))
      .then(body => readEdit(parseSkillJson(body))),
};
