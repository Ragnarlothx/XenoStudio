// Model + effort pickers in the composer (next to "Send to"). Backend: ModelCatalog.cs, requests modelCatalog / setModel.
// Changes save straight away and apply from each AI's next reply.
window.studioModels = (() => {
  const host = window.chrome?.webview;
  const send = (o) => host && host.postMessage(o);
  const AIS = [
    { id: 'claude', label: 'Claude', enabledKey: 'claudeEnabled', modelKey: 'claudeModel', effortKey: 'claudeEffort' },
    { id: 'codex', label: 'Codex', enabledKey: 'codexEnabled', modelKey: 'codexModel', effortKey: 'codexReasoning' },
  ];
  const EFFORT_NAMES = { '': 'default effort', low: 'low', medium: 'medium', high: 'high', xhigh: 'extra high', max: 'max', ultra: 'ultra (delegates)' };
  let catalog = null, settings = {}, seq = 0;

  const wrap = document.createElement('span');
  wrap.id = 'modelPick';
  const targets = document.getElementById('targets');
  targets.insertBefore(wrap, targets.querySelector('.grow'));

  const ui = {};
  for (const ai of AIS) {
    const box = document.createElement('span'); box.className = 'mp mp-' + ai.id;
    const name = document.createElement('span'); name.className = 'mp-name'; name.textContent = ai.label;
    const model = document.createElement('select'); model.setAttribute('aria-label', ai.label + ' model');
    const effort = document.createElement('select'); effort.setAttribute('aria-label', ai.label + ' reasoning effort');
    box.append(name, model, effort); wrap.append(box);
    ui[ai.id] = { box, model, effort };
    model.addEventListener('change', () => { fillEfforts(ai, model.value, effort.value); save(ai); });
    effort.addEventListener('change', () => save(ai));
  }

  function models(ai) { return catalog?.[ai.id]?.models || []; }

  function fillModels(ai) {
    const { model } = ui[ai.id];
    const current = settings[ai.modelKey] || '';
    model.replaceChildren();
    const list = models(ai);
    for (const m of list) { const o = new Option(m.name || m.id || 'Default', m.id); o.title = m.description || ''; model.append(o); }
    // a model typed in Settings that the catalog doesn't list still shows (and stays selected)
    if (current && !list.some((m) => m.id === current)) model.append(new Option(current + ' (custom)', current));
    model.value = current;
    model.title = list.find((m) => m.id === current)?.description || '';
  }

  function fillEfforts(ai, modelId, keep) {
    const { effort } = ui[ai.id];
    const m = models(ai).find((x) => x.id === modelId);
    const levels = m?.efforts?.length ? m.efforts : ['low', 'medium', 'high', 'xhigh', 'max'];
    effort.replaceChildren(new Option(m?.defaultEffort ? `default (${m.defaultEffort})` : 'default effort', ''));
    for (const l of levels) effort.append(new Option(EFFORT_NAMES[l] || l, l));
    effort.value = levels.includes(keep) ? keep : '';   // a level the new model doesn't support falls back to its default
    effort.title = effort.value === 'ultra' ? 'Ultra lets Codex delegate to sub-agents automatically: slower and uses more of your plan.' : '';
  }

  function render() {
    for (const ai of AIS) {
      const enabled = !!settings[ai.enabledKey];
      ui[ai.id].box.classList.toggle('off', !enabled);
      ui[ai.id].model.disabled = ui[ai.id].effort.disabled = !enabled || !catalog;
      if (!catalog) continue;
      fillModels(ai);
      fillEfforts(ai, settings[ai.modelKey] || '', settings[ai.effortKey] || '');
    }
  }

  function save(ai) {
    const { model, effort } = ui[ai.id];
    effort.title = effort.value === 'ultra' ? 'Ultra lets Codex delegate to sub-agents automatically: slower and uses more of your plan.' : '';
    send({ cmd: 'setModel', rid: 'model-' + ++seq, ai: ai.id, model: model.value, effort: effort.value });
  }

  host?.addEventListener('message', (e) => {
    const d = e.data;
    if (d.type === 'init' || d.type === 'settings') { settings = d.settings || settings; render(); if (d.type === 'init' && !catalog) load(); }
    if (d.type === 'modelCatalog' && d.rid?.startsWith('catalog-')) { if (d.ok) catalog = d.data; render(); }
    if (d.type === 'setModel' && !d.ok) window.alert?.(d.error || 'Couldn’t change the model.');
  });

  function load() { send({ cmd: 'modelCatalog', rid: 'catalog-' + ++seq }); }
  return { reload: () => { catalog = null; load(); } };
})();
