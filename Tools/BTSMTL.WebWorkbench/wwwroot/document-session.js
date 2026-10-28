export function candidateAcceptance(receipt) {
  return receipt.saveReady ? '完整领域已接收' : '仅结构接收 · 尚缺完整领域接收，暂不能保存';
}

export class DocumentEditSession {
  constructor(snapshot, rootPath, definitionsRevision, send) {
    this.context = { rootPath, rootRevision: snapshot.revision, definitionsRevision };
    this.send = send;
    this.baseline = snapshot.files;
    this.steps = [];
    this.redoSteps = [];
    this.savedUndo = [];
    this.savedRedo = [];
    this.receipt = null;
    this.result = null;
    this.pendingCommit = null;
    this.busy = false;
  }

  get changed() { return this.receipt?.differences.length > 0; }
  get unsettled() { return this.steps.length > 0 || this.changed || this.pendingCommit !== null; }
  get canEdit() { return !this.receipt || this.receipt.historyDirection === 'edit'; }
  get canSave() { return this.changed && this.receipt.saveReady && this.result === null; }
  get canUndo() { return this.canEdit && (this.steps.length > 0 || this.savedUndo.length > 0); }
  get canRedo() { return this.canEdit && (this.redoSteps.length > 0 ||
    (this.steps.length === 0 && this.savedRedo.length > 0)); }

  apply(documentId, edits) {
    return this.preview('edits', { graphBatches: [{ documentId, edits }], timelineBatches: [] });
  }

  applyTimeline(documentId, edits) {
    return this.preview('edits', { graphBatches: [], timelineBatches: [{ documentId, edits }] });
  }

  applyFiles(files) {
    return this.preview('candidates', { files });
  }

  async preview(endpoint, request) {
    await this.perform(async () => {
      if (!this.canEdit) throw new Error('请先处理当前撤销或重做候选，再继续编辑。');
      const receipt = await this.send(`/api/authoring/documents/${endpoint}`, { ...this.context,
        requestId: crypto.randomUUID(), baseCandidateId: this.receipt?.candidateId ?? null, ...request });
      this.steps = [...this.steps, receipt.candidateId];
      this.redoSteps = [];
      this.receipt = { ...receipt, historyDirection: 'edit' };
      this.result = null;
    });
  }

  undo() {
    return this.steps.length ? this.restore(this.steps.slice(0, -1), [...this.redoSteps, this.steps.at(-1)])
      : this.inverse('undo', this.savedUndo.at(-1));
  }

  redo() {
    return this.redoSteps.length ? this.restore([...this.steps, this.redoSteps.at(-1)], this.redoSteps.slice(0, -1))
      : this.inverse('redo', this.savedRedo.at(-1));
  }

  async restore(steps, redoSteps) {
    await this.perform(async () => {
      if (!this.canEdit) throw new Error('请先处理当前撤销或重做候选，再继续编辑。');
      const receipt = steps.length
        ? await this.send(`/api/authoring/documents/candidates/${steps.at(-1)}`) : null;
      this.steps = steps;
      this.redoSteps = redoSteps;
      this.receipt = receipt ? { ...receipt, historyDirection: 'edit' } : null;
      this.result = null;
    });
  }

  async inverse(direction, appliedCandidateId) {
    await this.perform(async () => {
      const current = await this.send(`/api/workspace/root?path=${encodeURIComponent(this.context.rootPath)}`);
      const receipt = await this.send('/api/authoring/documents/inverse', { ...this.context,
        rootRevision: current.revision, requestId: crypto.randomUUID(), appliedCandidateId });
      this.receipt = { ...receipt, historyDirection: direction };
      this.result = null;
      if (!receipt.saveReady) return;
      this.pendingCommit = this.receipt;
      await this.submitCommit();
    });
  }

  async commit() {
    await this.perform(async () => {
      this.pendingCommit = this.receipt;
      await this.submitCommit();
    });
  }

  async retryCommit() {
    await this.perform(() => this.submitCommit(), true);
  }

  async queryCommit() {
    await this.perform(async () => {
      const result = await this.send(`/api/authoring/documents/requests/${this.pendingCommit.requestId}`);
      await this.acceptCommit(result);
    }, true);
  }

  async submitCommit() {
    const result = await this.send(`/api/authoring/documents/candidates/${this.pendingCommit.candidateId}/commit`, {});
    await this.acceptCommit(result);
  }

  async acceptCommit(result) {
    this.result = result;
    if (result.status !== 'Saved') { this.pendingCommit = null; return; }
    const snapshot = await this.send(`/api/workspace/root?path=${encodeURIComponent(this.context.rootPath)}`);
    const { candidateId, historyDirection: direction } = this.pendingCommit;
    if (direction === 'undo') { this.savedUndo.pop(); this.savedRedo.push(candidateId); }
    else if (direction === 'redo') { this.savedRedo.pop(); this.savedUndo.push(candidateId); }
    else { this.savedUndo.push(candidateId); this.savedRedo = []; }
    this.context.rootRevision = snapshot.revision;
    this.baseline = snapshot.files.map(file => ({ ...file, document: JSON.parse(file.content), problem: null }));
    this.steps = [];
    this.redoSteps = [];
    this.receipt = null;
    this.pendingCommit = null;
  }

  files() {
    if (!this.receipt) return this.baseline;
    const originals = new Map(this.baseline.map(file => [file.path, file]));
    return this.receipt.files.map(file => {
      const original = originals.get(file.path);
      return { path: file.path, revision: original?.revision || '', content: file.text,
        document: JSON.parse(file.text), problem: null, draftChanged: original?.content !== file.text };
    });
  }

  discard() {
    if (this.busy || this.pendingCommit) throw new Error('保存结果尚未确认，请先按原请求查询结果。');
    this.steps = [];
    this.redoSteps = [];
    this.receipt = null;
    this.result = null;
  }

  async perform(action, resolvingCommit = false) {
    if (this.busy) throw new Error('上一项作者操作尚未返回。');
    if (this.pendingCommit && !resolvingCommit) throw new Error('保存结果尚未确认，请先按原请求查询结果。');
    this.busy = true;
    try { await action(); }
    finally { this.busy = false; }
  }
}
