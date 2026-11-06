import { postText } from './http';
import { array, boolean, nullable, number, object, text, type Decoder } from './playerRecords';
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
const variable = object({ m_Declaration: declaration, m_Name: text, m_TypeId: text,
  m_DefaultValue: parsedJson, m_IsPublic: boolean, m_DeclarationOrder: number });
export const readVariablesValue = object({ m_ContentDomain: text, m_OwnerId: text, m_Revision: text, m_Variables: array(variable) });
const choices = object({
  types: array(object({ typeId: text, clrType: text, valueKind: text, defaultValue: parsedJson, enumMembers: nullable(array(enumMember)) })),
  scopes: array(object({ name: text, value: number, defaultLifetime: number, lifetimes: array(number) })),
  lifetimes: array(enumMember), inputProjections: array(enumMember),
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
  | { kind: 'create'; typeId: string; name?: string }
  | { kind: 'rename'; variableId: string; name: string }
  | { kind: 'set-default'; variableId: string; value: JsonValue }
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
