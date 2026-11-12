// XenoStudio UI — talks to the C# host over chrome.webview messages.
(() => {
  const $ = id => document.getElementById(id);
  const host = window.chrome && window.chrome.webview;
  const send = o => host && host.postMessage(o);
  const NAMES = { xeno: 'Xeno', claude: 'Claude', codex: 'Codex', deepseek: 'DeepSeek', grok:'Grok', system: 'System', shell: 'Terminal' };
  const AV = { xeno: 'X', claude: 'C', codex: 'Cx', deepseek: 'D', grok:'G', shell: '>' };

  let settings = {}, chat = { id: null, messages: [] }, target = 'auto', busy = false;
  const UPLOADS = 'https://uploads.xenostudio.app/';   // data\uploads, mapped by the host
  const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp'];
  const MAX_IMAGE = 20 * 1024 * 1024;
  let pending = [];   // pictures waiting in the composer: { name, data }
  const agentKeys = {claude:'claudeEnabled',codex:'codexEnabled',deepseek:'deepSeekEnabled',grok:'grokEnabled'};
  const live = new Map();   // msg id -> { el, text, tools[] }

  // -------------------------------------------------------------- rendering
  const fmtTime = ts => new Date(ts).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const nearBottom = () => { const l = $('log'); return l.scrollHeight - l.scrollTop - l.clientHeight < 80; };
  const toBottom = (force) => { const l = $('log'); if (force || (window.studioUi?.autoScroll !== false && nearBottom())) l.scrollTop = l.scrollHeight; };

  function msgEl(m) {
    if (m.who === 'system') {
      const d = document.createElement('div');
      d.className = 'm sys'; d.id = 'm-' + m.id; d.textContent = m.text;
      return d;
    }
    const d = document.createElement('div');
    d.className = 'm ' + m.who + (m.error ? ' err' : '');
    d.id = 'm-' + m.id;
    d.innerHTML =
      '<div class="av">' + (AV[m.who] || '?') + '</div>' +
      '<div class="body"><div class="hd"><span class="who">' + (m.who === 'xeno' ? 'You' : NAMES[m.who] || 'Message') + '</span><span class="when">' + fmtTime(m.ts) +
      '</span><button class="copy" title="Copy message">Copy</button></div><div class="tools"></div><div class="txt"></div><div class="imgs"></div></div>';
    d.querySelector('.copy').onclick = () => copy(chat.messages.find(x => x.id === m.id)?.text || live.get(m.id)?.text || '');
    d.querySelector('.who').textContent = m.who === 'xeno' ? (settings.userName || 'You') : (NAMES[m.who] || 'Message');
    const imgs = d.querySelector('.imgs');
    for (const rel of m.images || []) {
      const img = document.createElement('img');
      img.src = UPLOADS + rel.split('/').map(encodeURIComponent).join('/');
      img.alt = img.title = 'Attached picture (click to open)';
      img.onclick = () => send({ cmd: 'openImage', path: rel });
      imgs.appendChild(img);
    }
    imgs.style.display = (m.images || []).length ? '' : 'none';
    fill(d, m.text, m.tools || [], false);
    const changes=document.createElement('div');changes.className='changeCards';window.codePreview.cards(changes,m.files);d.querySelector('.body').append(changes);
    return d;
  }

  function fill(d, text, tools, typing) {
    const t = d.querySelector('.tools');
    t.innerHTML = '';
    for (const x of tools) { const s = document.createElement('span'); s.className = 'tool'; s.title = x; s.textContent = x; t.appendChild(s); }
    t.style.display = tools.length ? '' : 'none';
    const txt = d.querySelector('.txt');
    if (d.classList.contains('shell') || window.studioUi?.renderMarkdown === false) { txt.textContent = text || ''; } else txt.innerHTML = window.md(text || '');
    window.codePreview.enhance(txt, t);
    txt.classList.toggle('typing', !!typing);
    if (typing && !text) txt.innerHTML = '<span class="muted">' + (tools.length ? 'working…' : 'thinking…') + '</span>';
  }

  function renderChat() {
    window.codePreview.clearAttachment();
    const log = $('log');
    log.innerHTML = '';
    live.clear();
    if (!chat.messages.length) {
      log.innerHTML = '<div class="empty"><h2>Claude + Codex</h2>Switch assistants on or off above. Enabled assistants answer in turn. ' +
        'Each sees what the other said.<br>Use <b>@claude</b> , <b>@codex</b> or <b>@deepseek</b> or <b>@grok</b> to talk to one. They can hand off to each other the same way.<br>' +
        '<span class="mono small">Claude and Codex use their CLIs. Backup assistants use your configured API or local endpoint.</span></div>';
    }
    for (const m of chat.messages) log.appendChild(msgEl(m));
    toBottom(true);
    renderChats(window._chats || []);
  }

  function renderChats(list) {
    window._chats = list;
    const query = ($('chatSearch').value || '').toLowerCase();
    const el = $('chatList');
    el.innerHTML = '';
    for (const c of list.filter(c => c.title.toLowerCase().includes(query))) {
      const d = document.createElement('div');
      d.className = 'ci' + (c.id === chat.id ? ' on' : '');
      d.innerHTML = '<span class="t"></span><button class="del" title="Delete chat">✕</button>';
      d.querySelector('.t').textContent = c.title;
      d.title = c.title + '\n' + new Date(c.updated).toLocaleString() + ' · ' + c.count + ' messages';
      d.onclick = () => c.id !== chat.id && send({ cmd: 'openChat', id: c.id });
      d.querySelector('.del').onclick = e => { e.stopPropagation(); if (window.studioUi?.confirmDelete === false || confirm('Delete "' + c.title + '"?')) send({ cmd: 'deleteChat', id: c.id }); };
      el.appendChild(d);
    }
  }

  function applySettings(s, exe) {
    settings = s;
    for (const [id,key] of Object.entries(agentKeys)) {
      const enabled = !!s[key];
      const pill = document.querySelector('[data-agent="'+id+'"]');
      pill.setAttribute('aria-checked', String(enabled));
      pill.classList.toggle('off', !enabled);
      pill.title = NAMES[id] + (enabled ? ': on. Click to disable.' : ': off. Click to enable.');
      const chip = document.querySelector('[data-t="'+id+'"]'); chip.disabled = !enabled;
      if (!enabled && target === id) { target='auto';document.querySelectorAll('#targets .chip').forEach(x=>x.classList.toggle('on',x.dataset.t==='auto')); }
    }
    const oldTarget = window.studioUi?.defaultTarget;
    window.studioUi = studioSettings.apply(s);
    if(oldTarget !== window.studioUi.defaultTarget) {
      target=window.studioUi.defaultTarget;
      if(agentKeys[target] && !s[agentKeys[target]])target='auto';
      document.querySelectorAll('#targets .chip').forEach(x=>x.classList.toggle('on',x.dataset.t===target));
    }
    for (const theme of ['light', 'cyberpunk', 'hacker']) document.body.classList.toggle(theme, s.theme === theme);
    window.codePreview.theme(s.theme);
    $('cwd').textContent = s.workDir;
    $('cwd').title = 'Shared working folder: ' + s.workDir;
    
  }

  let noticeTimer;
  function notice(text) {
    const n = $('notice');
    n.textContent = text; n.classList.remove('hidden');
    clearTimeout(noticeTimer); noticeTimer = setTimeout(() => n.classList.add('hidden'), 7000);
  }

  function copy(text) {
    navigator.clipboard.writeText(text).then(() => notice('Copied.'), () => notice('Copy failed.'));
  }

  // -------------------------------------------------------------- host messages
  host && host.addEventListener('message', e => {
    const d = e.data;
    studioSettings.receive(d);
    switch (d.type) {
      case 'init':
        window.studioMeta = 'Version ' + (d.version || '1.0') + ' · Data: ' + (d.dataDir || 'portable data folder');
        applySettings(d.settings, d.exe);
        window._chats = d.chats;
        chat = d.chat;
        renderChat();
        $('input').focus();
        break;
      case 'chat':
        chat = d.chat; window._chats = d.chats; renderChat(); $('input').focus();
        break;
      case 'chats': renderChats(d.chats); break;
      case 'settings': applySettings(d.settings, d.exe); notice('Settings saved.'); break;
      case 'pickedDir': break;
      case 'notice': notice(d.text); break;
      case 'msg': {
        if (!chat.messages.length) $('log').innerHTML = '';
        chat.messages.push(d.msg);
        $('log').appendChild(msgEl(d.msg));
        toBottom(d.msg.who === 'xeno');
        break;
      }
      case 'start': {
        if (!chat.messages.length) $('log').innerHTML = '';
        const el = msgEl(d.msg);
        $('log').appendChild(el);
        live.set(d.msg.id, { el, text: '', tools: [] });
        fill(el, '', [], true);
        toBottom();
        break;
      }
      case 'reset': { const L = live.get(d.id); if (L) { L.text = ''; L.tools = []; fill(L.el, '', [], true); } break; }
      case 'delta': {
        const L = live.get(d.id); if (!L) break;
        const stick = window.studioUi?.autoScroll !== false && nearBottom();
        L.text += d.text; fill(L.el, L.text, L.tools, true);
        if (stick) toBottom(true);
        break;
      }
      case 'aiFile': {const L=live.get(d.id);if(L){let chip=L.el.querySelector('.editingFile');if(!chip){chip=document.createElement('div');chip.className='editingFile';L.el.querySelector('.body').append(chip);}chip.textContent=(NAMES[d.who]||'AI')+(d.phase==='editing'?' editing ':' edited ')+d.path;chip.classList.toggle('active',d.phase==='editing');}break;}
      case 'tool': {
        const L = live.get(d.id); if (!L) break;
        const stick = window.studioUi?.autoScroll !== false && nearBottom();
        L.tools.push(d.text); fill(L.el, L.text, L.tools, true);
        if (stick) toBottom(true);
        break;
      }
      case 'done': {
        const L = live.get(d.msg.id);
        chat.messages.push(d.msg);
        live.delete(d.msg.id);
        const el = msgEl(d.msg);
        const stick = window.studioUi?.autoScroll !== false && nearBottom();
        if (L) L.el.replaceWith(el); else $('log').appendChild(el);
        if (stick) toBottom(true);
        break;
      }
      case 'status': {
        busy = d.busy;
        for (const [k, v] of Object.entries(d.agents)) {
          const p = document.querySelector('[data-agent="'+k+'"]');
          if (!p) continue;
          p.className = 'pill ' + v;
          p.title = NAMES[k] + ': ' + v + '. Click to toggle.';
        }
        $('bStop').classList.toggle('hidden', !busy);
        break;
      }
    }
  });

  // -------------------------------------------------------------- input
  function newChat() { if(!window.studioUi?.confirmNew || confirm('Start a new chat? This conversation stays saved.')) send({cmd:'newChat'}); }
  // -------------------------------------------------------------- pictures (paste, drop, + Picture)
  function renderPending() {
    const el = $('attachments');
    el.innerHTML = '';
    pending.forEach((p, i) => {
      const t = document.createElement('div');
      t.className = 'att';
      t.innerHTML = '<img alt=""><button title="Remove">✕</button>';
      t.querySelector('img').src = p.data;
      t.querySelector('img').title = p.name;
      t.querySelector('button').onclick = () => { pending.splice(i, 1); renderPending(); };
      el.appendChild(t);
    });
    el.classList.toggle('hidden', pending.length === 0);
  }

  function addFiles(files) {
    for (const f of files) {
      const extensionTypes = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', webp: 'image/webp', bmp: 'image/bmp' };
      const imageType = f.type || extensionTypes[(f.name || '').split('.').pop().toLowerCase()];
      if (!IMAGE_TYPES.includes(imageType)) { notice('Only PNG, JPG, GIF, WebP or BMP pictures can be attached.'); continue; }
      if (f.size > MAX_IMAGE) { notice(f.name + ' is over 20 MB.'); continue; }
      const r = new FileReader();
      r.onload = () => { pending.push({ name: f.name || 'pasted.png', data: r.result }); renderPending(); };
      r.onerror = () => notice('Could not read ' + (f.name || 'picture') + '. Try attaching it again.');
      r.readAsDataURL(f.type ? f : f.slice(0, f.size, imageType));
    }
  }

  $('bAttach').onclick = () => $('fileInput').click();
  $('fileInput').onchange = e => { addFiles([...e.target.files]); e.target.value = ''; $('input').focus(); };
  $('input').addEventListener('paste', e => {
    const files = [...(e.clipboardData?.items || [])].filter(it => it.kind === 'file').map(it => it.getAsFile()).filter(Boolean);
    if (files.length) { e.preventDefault(); addFiles(files); }
  });
  document.addEventListener('dragover', e => {
    if (![...(e.dataTransfer?.types || [])].includes('Files')) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
    document.body.classList.add('dropping');
  });
  document.addEventListener('dragleave', e => { if (!e.relatedTarget) document.body.classList.remove('dropping'); });
  window.addEventListener('blur', () => document.body.classList.remove('dropping'));
  document.addEventListener('dragend', () => document.body.classList.remove('dropping'));
  document.addEventListener('drop', e => {
    document.body.classList.remove('dropping');
    if (!e.dataTransfer?.files?.length) return;
    e.preventDefault();
    addFiles([...e.dataTransfer.files]);
    $('input').focus();
  });

  function doSend() {
    const v = $('input').value + window.codePreview.context();
    if (pending.length) {
      send({ cmd: 'send', text: v, target, images: pending });
      pending = []; renderPending(); window.codePreview.clearAttachment();
      $('input').value = ''; autosize();
      return;
    }
    if (!v.trim()) return;
    const command = v.trim().toLowerCase();
    const shortcuts = {'/new':'newChat','/export':'export','/stop':'stop'};
    if (shortcuts[command]) { if(command==='/new')newChat();else send({cmd:shortcuts[command]}); $('input').value=''; autosize(); return; }
    if(command==='/settings') {studioSettings.open();$('input').value='';autosize();return;}
    if(['/claude','/codex','/deepseek','/grok','/all','/both'].includes(command)) {document.querySelector('[data-t="'+command.slice(1)+'"]').click();$('input').value='';autosize();return;}
    send({ cmd: 'send', text: v, target });
    window.codePreview.clearAttachment();
    $('input').value = '';
    autosize();
  }
  function autosize() { const t = $('input'); t.style.height = 'auto'; t.style.height = Math.min(t.scrollHeight + 2, window.innerHeight * 0.4) + 'px'; }

  $('input').addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && (window.studioUi?.enterToSend !== false || e.ctrlKey || e.metaKey)) { e.preventDefault(); doSend(); }
  });
  $('input').addEventListener('input', autosize);
  document.querySelectorAll('[data-agent]').forEach(b => b.onclick = () => {
    const key = agentKeys[b.dataset.agent];
    const updated = {...settings, [key]:!settings[key]};
    applySettings(updated);
    send({cmd:'saveSettings',settings:updated});
  });
  $('bSend').onclick = doSend;
  $('bStop').onclick = () => send({ cmd: 'stop' });
  $('bNew').onclick = newChat;
  $('bExport').onclick = () => send({ cmd: 'export' });
  $('bFolder').onclick = () => send({ cmd: 'openDir' });

  document.querySelectorAll('#targets .chip').forEach(b => b.onclick = () => {
    target = b.dataset.t;
    document.querySelectorAll('#targets .chip').forEach(x => x.classList.toggle('on', x === b));
    $('input').focus();
  });

  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') { if (!$('modal').classList.contains('hidden')) closeSettings(); else if (busy) send({ cmd: 'stop' }); }
    if (e.ctrlKey && e.key.toLowerCase() === 'n') { e.preventDefault(); newChat(); }
  });

  $('log').addEventListener('click', e => {
    const a = e.target.closest('a');
    if (a) { e.preventDefault(); send({ cmd: 'openUrl', url: a.href }); return; }
    const cp = e.target.closest('pre .cp');
    if (cp) copy(cp.parentElement.querySelector('code').textContent);
  });

  const closeSettings = () => studioSettings.close();
  $('chatSearch').oninput = () => renderChats(window._chats || []);
  studioSettings.init();
  send({ cmd: 'ready' });
})();
