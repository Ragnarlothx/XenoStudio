// One activity timeline per assistant reply in the selected chat.
window.createActivityPanel = (names, agentKeys, formatTime) => {
  const panel = document.getElementById('activity');
  const headline = document.getElementById('activityHeadline');
  const models = document.getElementById('activityModels');
  const queueLine = document.getElementById('activityQueue');
  const log = document.getElementById('activityLog');
  const replies = new Map();
  const dirty = new Set();
  const kinds = { think: 'Thinking', tool: 'Tool', retry: 'Retry' };
  const seats = Object.keys(agentKeys);
  let chatId, status, active, settings = {};
  let showThinking = true, autoScroll = true, markdown = true;
  let frame = 0, timer = 0, scrollToLatest = false;

  function element(tag, className, text = '') {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text) node.textContent = text;
    return node;
  }

  const empty = element('p', 'activity-empty', 'No assistant replies in this chat yet.');
  log.append(empty);
  const rows = seats.map(id => {
    const row = element('li', 'activity-model');
    row.dataset.model = id;
    const state = element('span', 'activity-state');
    const elapsed = element('span', 'activity-elapsed');
    row.append(element('span', 'activity-model-name', names[id]), state, elapsed);
    models.append(row);
    return { id, row, state, elapsed };
  });

  function elapsed(ms) {
    const seconds = Math.max(0, Math.floor(ms / 1000));
    const minutes = Math.floor(seconds / 60);
    const tail = String(seconds % 60).padStart(2, '0');
    return minutes < 60
      ? minutes + ':' + tail
      : Math.floor(minutes / 60) + ':' + String(minutes % 60).padStart(2, '0') + ':' + tail;
  }

  const runningModel = () => active?.msg.who || status?.running || seats.find(id => status?.agents?.[id] === 'running');
  const phase = () => active?.phase === 'think' && showThinking ? 'Thinking' :
    active?.phase === 'retry' ? 'Retrying' : active?.phase === 'reply' ? 'Replying' : 'Working';
  const following = () => panel.open && autoScroll && log.scrollHeight - log.scrollTop - log.clientHeight < 40;

  function paintTimers() {
    const running = runningModel();
    const now = Date.now();
    const since = active?.msg.ts || status?.runningSince;
    for (const model of rows) model.elapsed.textContent = model.id === running && since > 0 ? elapsed(now - since) : '';
    if (active?.msg.ts > 0) active.duration.textContent = elapsed(now - active.msg.ts);
  }

  function paintStatus() {
    const running = runningModel();
    const waiting = (status?.queue || seats.filter(id => status?.agents?.[id] === 'queued')).filter(id => seats.includes(id));
    const work = phase();
    for (const { id, row, state } of rows) {
      const stateName = id === running ? 'running' : waiting.includes(id) ? 'queued' : settings[agentKeys[id]] ? 'idle' : 'off';
      row.dataset.state = stateName;
      state.textContent = { running: work, queued: 'Queued', idle: 'Idle', off: 'Off' }[stateName];
    }
    const caption = running ? names[running] + ' ' + work.toLowerCase() : waiting.length ? 'Queued' : 'Idle';
    if (headline.textContent !== caption) headline.textContent = caption;
    headline.dataset.running = String(!!running);
    const next = waiting.length ? 'Next: ' + waiting.map(id => names[id]).join(' → ') : '';
    if (queueLine.textContent !== next) queueLine.textContent = next;
    queueLine.hidden = !next;
    paintTimers();
    if (running && !timer) timer = setInterval(paintTimers, 1000);
    else if (!running && timer) { clearInterval(timer); timer = 0; }
  }

  function schedule(reply) {
    if (reply) dirty.add(reply);
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      const stick = scrollToLatest || following();
      if (panel.open) {
        for (const item of dirty) if (item.details.open) paintSteps(item);
      }
      dirty.clear();
      paintStatus();
      if (stick && panel.open) log.scrollTop = log.scrollHeight;
      scrollToLatest = false;
    });
  }

  function savedSteps(msg) {
    if (Array.isArray(msg.trace) && msg.trace.length) {
      return msg.trace.filter(step => Object.hasOwn(kinds, step.kind)).map(step => ({ ...step }));
    }
    return (msg.tools || []).map(text => ({ kind: 'tool', text, ts: 0 }));
  }

  function paintHeader(reply) {
    const { msg, state, duration } = reply;
    reply.details.classList.toggle('running', reply === active);
    reply.details.classList.toggle('failed', !!msg.error);
    state.textContent = reply === active ? phase() : msg.error ? 'Error' :
      !msg.doneTs && msg.trace?.length ? 'Interrupted' : '';
    duration.textContent = msg.doneTs > 0 && msg.ts > 0 ? elapsed(msg.doneTs - msg.ts) : '';
    if (reply === active && msg.ts > 0) duration.textContent = elapsed(Date.now() - msg.ts);
  }

  function paintSteps(reply) {
    const steps = showThinking ? reply.steps : reply.steps.filter(step => step.kind !== 'think');
    reply.empty.hidden = steps.length > 0;
    reply.list.hidden = !steps.length;
    if (!steps.length) {
      reply.empty.textContent = reply.steps.length ? 'Thinking summaries are hidden.' :
        reply === active ? 'Waiting for activity…' : 'No activity was saved for this reply.';
    }
    steps.forEach((step, index) => {
      const previous = reply.painted[index];
      if (previous?.kind === step.kind && previous.ts === step.ts && previous.text === step.text && previous.markdown === markdown) return;
      const item = element('li', 'activity-step');
      item.dataset.kind = step.kind;
      const heading = element('div', 'activity-step-heading', kinds[step.kind]);
      if (step.ts > 0 && reply.msg.ts > 0) heading.append(element('span', 'when', elapsed(step.ts - reply.msg.ts)));
      const content = element('div', 'activity-step-text');
      if (step.kind === 'think' && markdown) {
        content.classList.add('txt');
        content.innerHTML = window.md(step.text);
        window.codePreview.enhance(content, content);
      } else content.textContent = step.text;
      item.append(heading, content);
      if (previous) reply.list.children[index].replaceWith(item);
      else reply.list.append(item);
      reply.painted[index] = { ...step, markdown };
    });
    while (reply.list.children.length > steps.length) reply.list.lastElementChild.remove();
    reply.painted.length = steps.length;
  }

  function addReply(msg) {
    const details = element('details', 'activity-reply');
    const summary = element('summary', 'activity-reply-summary');
    const label = element('strong', '', names[msg.who]);
    const state = element('span', 'activity-reply-state');
    const duration = element('span', 'activity-elapsed');
    summary.append(label, element('span', 'when', msg.ts > 0 ? formatTime(msg.ts) : ''), state, duration);
    const reply = {
      msg, details, state, duration, steps: savedSteps(msg), painted: [],
      empty: element('p', 'activity-empty'),
      list: element('ol', 'activity-steps')
    };
    details.append(summary, reply.empty, reply.list);
    details.addEventListener('toggle', () => { if (details.open && panel.open) paintSteps(reply); });
    details.dataset.model = msg.who;
    log.append(details);
    empty.hidden = true;
    replies.set(msg.id, reply);
    paintHeader(reply);
    return reply;
  }

  function show(chat) {
    chatId = chat.id;
    status = active = null;
    replies.clear();
    dirty.clear();
    empty.hidden = false;
    log.replaceChildren(empty);
    for (const msg of chat.messages) if (seats.includes(msg.who)) addReply(msg);
    scrollToLatest = true;
    schedule();
  }

  function start(msg) {
    if (!seats.includes(msg.who)) return;
    scrollToLatest ||= following();
    active = replies.get(msg.id) || addReply(msg);
    active.phase = active.steps.at(-1)?.kind;
    active.details.open = true;
    paintHeader(active);
    schedule(active);
  }

  function trace(event) {
    if (!active || active.msg.id !== event.id || !Object.hasOwn(kinds, event.kind)) return;
    const last = active.steps.at(-1);
    if (event.append && last?.kind === 'think' && event.kind === 'think') last.text += event.text;
    else active.steps.push({ kind: event.kind, text: event.text, ts: event.ts });
    if (active.phase !== event.kind) {
      active.phase = event.kind;
      paintHeader(active);
    }
    schedule(active);
  }

  function replying(id) {
    if (!active || active.msg.id !== id || active.phase === 'reply') return;
    active.phase = 'reply';
    paintHeader(active);
    schedule();
  }

  function done(msg) {
    if (!seats.includes(msg.who)) return;
    const reply = replies.get(msg.id) || addReply(msg);
    if (active === reply) active = null;
    reply.msg = msg;
    reply.steps = savedSteps(msg);
    paintHeader(reply);
    schedule(reply);
  }

  function setStatus(update) {
    if (update.chatId != null && update.chatId !== chatId) return;
    status = update;
    // A reload can receive an in-progress reply as part of the initial chat snapshot.
    if (update.running && update.replyId && !active) {
      const reply = replies.get(update.replyId);
      if (reply && !reply.msg.doneTs) start(reply.msg);
    } else if (active && !update.busy) {
      const reply = active;
      active = null;
      paintHeader(reply);
      schedule(reply);
    }
    schedule();
  }

  function applySettings(nextSettings, ui) {
    const formatChanged = showThinking !== (nextSettings.showThinking !== false) || markdown !== (ui.renderMarkdown !== false);
    settings = nextSettings;
    showThinking = settings.showThinking !== false;
    autoScroll = ui.autoScroll !== false;
    markdown = ui.renderMarkdown !== false;
    if (formatChanged) {
      if (active) paintHeader(active);
      for (const reply of replies.values()) if (reply.details.open) dirty.add(reply);
    }
    schedule();
  }

  panel.addEventListener('toggle', () => {
    if (!panel.open) return;
    for (const reply of replies.values()) if (reply.details.open) dirty.add(reply);
    scrollToLatest ||= autoScroll && !!active;
    schedule();
  });
  return { show, start, trace, replying, done, setStatus, applySettings };
};
