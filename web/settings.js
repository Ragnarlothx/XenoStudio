// Settings are edited as a draft; only Save writes to the host.
window.studioSettings = (() => {
  const $ = id => document.getElementById(id);
  const send = data => window.chrome?.webview?.postMessage(data);
  let current = {}, draft = {}, servers = [], active = 'General';
  let promptDefaults = {template:'',footer:'',placeholders:[]}, promptFocus = 'promptTemplate', previewId = 'claude', previewPending = null;
  const groups = {
    General: [
      ['userName','Your display name'], ['xenoEditPath','XenoEdit executable path (blank = auto-detect)'], ['workDir','Shared working folder (several allowed: D:\\Projects,E:\\Work - the first is where the AIs start)'],
      ['firstSpeaker','Who answers first','select','claude,codex,alternate'],
      ['maxAiTurns','Maximum consecutive AI replies','number',1,50],
      ['turnTimeoutSec','Turn time limit in seconds (0 = unlimited)','number',0,86400],
      ['retryOnError','Retries before calling a backup','number',0,5],
      ['retryDelaySec','Seconds between retries','number',0,300],
      ['handoffEnabled','Allow assistants to hand off with @mentions','check'],
      ['resumeHistory','Recent messages to resend after a session restart','number',1,500],
      ['autoExport','Automatically export chats','check'], ['systemExtra','Instructions for both assistants','area']
    ],
    Claude: [
      ['claudeEnabled','Enable Claude','check'], ['claudeUseBundled','Use pinned runtime\\claude.exe','check'],
      ['claudeModel','Model (blank = CLI default)'], ['claudeFallbackModel','Fallback model'],
      ['claudeMode','Permissions','select','read,edit,full'], ['claudeExtraDirs','Additional folders (, or ; separated)'],
      ['claudePath','Custom executable path'], ['claudeMaxTurns','Agent steps per reply (0 = default)','number',0,500],
      ['claudeAllowedTools','Allowed tools (comma separated)'], ['claudeDisallowedTools','Blocked tools (comma separated)'],
      ['claudeStrictMcp','Only use this app’s MCP servers','check'], ['claudeAutoAllowMcp','Preapprove this app’s MCP tools','check'],
      ['claudeSystemExtra','Claude instructions','area'], ['claudeExtraArgs','Extra CLI arguments'], ['claudeEnv','Environment variables (KEY=VALUE per line)','area']
    ],
    Codex: [
      ['codexEnabled','Enable Codex','check'], ['codexUseBundled','Use pinned runtime\\codex.exe','check'],
      ['codexModel','Model (blank = CLI default)'], ['codexReasoning','Reasoning effort','select',',minimal,low,medium,high'],
      ['codexSandbox','Access','select','read-only,workspace-write,danger-full-access'],
      ['codexPath','Custom executable path'], ['codexUseConfig','Also load your installed Codex configuration','check'],
      ['codexSystemExtra','Codex instructions','area'], ['codexExtraArgs','Extra CLI arguments'], ['codexEnv','Environment variables (KEY=VALUE per line)','area']
    ],
    DeepSeek: [
      ['deepSeekEnabled','Enable DeepSeek for direct messages','check'],
      ['deepSeekAutoReply','Also answer regular messages (after primary assistants)','check'],
      ['deepSeekFallback','Use DeepSeek when a primary assistant fails (even if direct messages are off)','check'],
      ['deepSeekFallbackOn','Fallback trigger','select','any,limits'], ['deepSeekApiKey','API key (blank uses DEEPSEEK_API_KEY)','password'],
      ['deepSeekBaseUrl','API endpoint'], ['deepSeekModel','Model'],
      ['deepSeekMaxTokens','Maximum response tokens (0 = server default)','number',0,1000000],
      ['deepSeekShowReasoning','Show reasoning activity','check'], ['deepSeekSystemExtra','DeepSeek instructions','area']
    ],
    Grok: [
      ['grokEnabled','Enable Grok for direct messages','check'],
      ['grokAutoReply','Also answer regular messages (after primary assistants)','check'],
      ['grokFallback','Use Grok when a primary assistant fails (even if direct messages are off)','check'],
      ['grokFallbackOn','Fallback trigger','select','any,limits'],
      ['grokApiKey','API key (blank uses XAI_API_KEY)','password'],
      ['grokBaseUrl','API endpoint'], ['grokModel','Model'],
      ['grokMaxTokens','Maximum response tokens (0 = server default)','number',0,1000000],
      ['grokShowReasoning','Show reasoning activity','check'], ['grokSystemExtra','Grok instructions','area']
    ],
    Prompt: [['promptTemplate','Shared system prompt (blank = built-in default)','area'], ['promptFooter','Per-turn footer (blank = built-in default)','area']],
    MCP: [],
    Commands: [['shellEnabled','Enable !commands in chat','check'], ['shell','Command shell','select','powershell,pwsh,cmd'], ['shellShareWithAi','Include shell output in assistant context','check']],
    Appearance: [['theme','Theme','select','dark,light,cyberpunk,hacker'], ['ui.fontSize','Message font size','number',11,24], ['ui.accent','Accent color','color'], ['ui.density','Message spacing','select','comfortable,compact'], ['ui.timestamps','Show timestamps','check'], ['ui.tools','Show tool activity','check'], ['ui.sidebar','Show chat sidebar','check'], ['ui.enterToSend','Enter sends (otherwise Ctrl+Enter)','check']],
    Chat: [
      ['ui.defaultTarget','Default recipient','select','auto,both,claude,codex'],
      ['ui.autoScroll','Follow new replies automatically','check'],
      ['ui.renderMarkdown','Format messages as Markdown','check'],
      ['ui.confirmNew','Confirm before starting another chat','check'],
      ['ui.confirmDelete','Confirm chat deletion','check'],
      ['ui.showAvatars','Show participant avatars','check'],
      ['ui.showCopy','Always show message copy buttons','check'],
      ['ui.showBackup','Show backup DeepSeek and Grok in chat controls','check'],
      ['ui.composerRows','Composer height in lines','number',2,12]
    ],
    Reading: [
      ['ui.messageWidth','Message width in pixels','number',600,1600],
      ['ui.lineHeight','Line spacing (%)','number',120,220],
      ['ui.codeSize','Code font size','number',10,24],
      ['ui.wrapCode','Wrap long code lines','check'],
      ['ui.reduceMotion','Reduce animation','check'],
      ['ui.highContrast','Brighter secondary text','check']
    ],
    Runtime: []
  };
  const defaults = {fontSize:13,accent:'#4f9cf9',density:'comfortable',timestamps:true,tools:true,sidebar:true,enterToSend:true,defaultTarget:'auto',autoScroll:true,renderMarkdown:true,confirmNew:false,confirmDelete:true,showAvatars:true,showCopy:false,showBackup:false,composerRows:3,messageWidth:980,lineHeight:155,codeSize:12,wrapCode:false,reduceMotion:false,highContrast:false};
  function get(key) { return key.startsWith('ui.') ? draft.ui[key.slice(3)] : draft[key]; }
  function set(key,value) { if(key.startsWith('ui.')) draft.ui[key.slice(3)] = value; else draft[key] = value; }
  function button(text, action, parent) { const b=document.createElement('button'); b.type='button'; b.className='btn sec'; b.textContent=text; b.onclick=()=>{try{action();}catch(e){$('settingsError').textContent=e.message;}}; parent.append(b); return b; }
  function field(spec, parent, read=get, write=set) {
    const [key,title,type='text',options,max]=spec;
    const label=document.createElement('label'); label.textContent=title;
    const input=document.createElement(type==='area'?'textarea':type==='select'?'select':'input');
    input.id='setting-'+key; input.dataset.setting=key;
    if(type==='select') for(const value of options.split(',')) { const o=document.createElement('option'); o.value=value; o.textContent=key.endsWith('FallbackOn') ? ({any:'Any failure',limits:'Usage limits and outages only'}[value]||value) : value||'CLI default'; input.append(o); }
    else if(type==='area') input.rows=3;
    else input.type=type==='check'?'checkbox':type;
    if(type==='number') {input.min=options; input.max=max;}
    if(type==='check') {label.className='check'; input.checked=!!read(key);} else input.value=read(key)??'';
    input.oninput=()=>{ write(key,type==='check'?input.checked:type==='number'?Number(input.value):input.value); };
    label.append(input); parent.append(label);
    if(['promptTemplate','promptFooter'].includes(key)) {
      input.placeholder=promptDefaults[key==='promptTemplate'?'template':'footer'];
      input.addEventListener('focus',()=>{promptFocus=key;});
      input.addEventListener('input',invalidatePreview);
    }
    return input;
  }
  function invalidatePreview() {
    previewPending=null;
    if($('promptPreviewStatus')) $('promptPreviewStatus').textContent='Settings changed. Refresh preview to see the current draft.';
    if($('promptSystemPreview')) $('promptSystemPreview').textContent='';
    if($('promptFooterPreview')) $('promptFooterPreview').textContent='';
  }
  function previewPrompt() {
    captureMcp();
    previewPending={id:previewId,snapshot:JSON.stringify({...draft,mcpServers:servers})};
    $('promptPreviewStatus').textContent='Loading preview…';
    send({cmd:'previewPrompt',id:previewId,settings:{...structuredClone(draft),mcpServers:structuredClone(servers)}});
  }
  function renderPrompt(pane) {
    const note=document.createElement('p');note.className='muted';note.textContent='Blank fields use the built-in wording shown below. Placeholders also work in General instructions and each AI’s own instructions. Preview uses unsaved settings; Save applies them.';pane.append(note);
    button('Reset to default',()=>{draft.promptTemplate='';draft.promptFooter='';render();},pane);
    const chips=document.createElement('div');chips.className='prompt-chips';chips.setAttribute('aria-label','Insert a placeholder into the last focused prompt field');pane.append(chips);
    for(const raw of promptDefaults.placeholders) {
      const token='{'+String(raw).replace(/^\{|\}$/g,'')+'}';
      const b=button(token,()=>{
        const input=$('setting-'+promptFocus);if(!input)return;
        const start=input.selectionStart??input.value.length,end=input.selectionEnd??start;
        input.value=input.value.slice(0,start)+token+input.value.slice(end);
        draft[promptFocus]=input.value;input.focus();input.setSelectionRange?.(start+token.length,start+token.length);invalidatePreview();
      },chips);
      b.onmousedown=e=>e.preventDefault();
    }
    const details=document.createElement('details'), summary=document.createElement('summary');summary.textContent='Read the built-in defaults';details.append(summary);
    for(const [title,text] of [['System prompt',promptDefaults.template],['Turn footer',promptDefaults.footer]]){const h=document.createElement('h3');h.textContent=title;const pre=document.createElement('pre');pre.textContent=text;details.append(h,pre);}pane.append(details);
    const bar=document.createElement('div');bar.className='row';pane.append(bar);
    const select=field(['previewAI','Preview as','select','claude,codex,deepseek,grok'],bar,()=>previewId,(_,value)=>{previewId=value;previewPrompt();});select.id='promptPreviewAI';delete select.dataset.setting;
    button('Refresh preview',previewPrompt,bar);
    const status=document.createElement('p');status.id='promptPreviewStatus';status.setAttribute('role','status');pane.append(status);
    for(const [title,id] of [['Resolved system prompt','promptSystemPreview'],['Resolved turn footer','promptFooterPreview']]){const h=document.createElement('h3');h.textContent=title;const pre=document.createElement('pre');pre.id=id;pre.tabIndex=0;pane.append(h,pre);}
    previewPrompt();
  }
  function render() {
    const pane=$('settingsPane'); pane.replaceChildren();
    document.querySelectorAll('#settingsTabs button').forEach(b=>{b.classList.toggle('on',b.textContent===active); b.setAttribute('aria-selected',b.textContent===active);});
    const h=document.createElement('h2');h.textContent=active;pane.append(h);
    for(const spec of groups[active]) field(spec,pane);
    if(active==='Prompt') renderPrompt(pane);
    if(active==='General') button('Add working folder…',()=>send({cmd:'pickDir'}),pane);button('Clear folders',()=>{draft.workDir='';render();},pane);
    if(active==='Claude'||active==='Codex') button('Browse executable',()=>send({cmd:'pickFile',key:active.toLowerCase()+'Path',title:'Choose executable',filter:'Executables|*.exe|All files|*.*'}),pane);
    if(active==='Commands') {const p=document.createElement('p');p.className='muted';p.textContent='Run !dir or !git status in the shared folder. Shortcuts: /new, /export, /stop, /settings, /claude, /codex, /deepseek, /grok, /all, /both. CLI arguments and environment settings are applied on the next assistant turn.';pane.append(p);}
    if(active==='Grok') { const p=document.createElement('p');p.className='muted';p.textContent='Backup assistant using an xAI-compatible chat endpoint. Direct participation and automatic fallback are separate switches. Model names depend on your account or local server. Claude and Codex remain the primary assistants.';pane.append(p); }
    if(active==='DeepSeek') {
      const input=$('setting-deepSeekModel');input.setAttribute('list','deepseekModels');
      const list=document.createElement('datalist');list.id='deepseekModels';for(const name of ['deepseek-chat','deepseek-reasoner']){const o=document.createElement('option');o.value=name;list.append(o);}pane.append(list);
      const p=document.createElement('p');p.className='muted';p.textContent='Backup assistant. Claude and Codex are the primary participants. Uses your API account or a compatible local server. No Pro subscription is required by this app. Direct participation and automatic fallback are separate switches. Configure an endpoint before enabling either. For Ollama, use http://localhost:11434/v1 and the name of a model you have installed.';pane.append(p);
    }
    if(active==='MCP') renderMcp(pane);
    if(active==='Runtime') {
      const p=document.createElement('p');p.textContent='Pinned CLIs stay in runtime\\ until you choose to replace them with the installed versions. Account sign-in uses each CLI’s own authentication.';pane.append(p);
      const pre=document.createElement('pre');pre.id='runtimeDetails';pre.textContent=window.studioRuntime||'Loading runtime information…';pane.append(pre);
      for(const [text,cmd] of [['Refresh versions','runtimeInfo'],['Open app data','openData'],['Open Claude profile','openProfile'],['Export settings','exportSettings']]) button(text,()=>send({cmd}),pane);
      button('Update pinned CLIs from installed copies',()=>{if(confirm('Replace both pinned executables with the versions installed on this PC?'))send({cmd:'updateRuntime',which:'both'});},pane);
      button('Reset all settings',()=>{if(confirm('Reset saved settings to defaults?')) {send({cmd:'resetSettings'});close();}},pane);
      const meta=document.createElement('p');meta.className='muted';meta.textContent=window.studioMeta||'';pane.append(meta);
    }
  }
  function renderMcp(pane) {
    const bar=document.createElement('div');bar.className='row';pane.append(bar);
    button('Add server',()=>{captureMcp();servers.push({name:'server-'+(servers.length+1),type:'stdio',command:'',args:[],url:'',env:{},headers:{},enabled:true,claude:true,codex:true});render();},bar);
    button('Import installed servers',()=>send({cmd:'importMcp'}),bar);
    const note=document.createElement('p');note.className='muted';note.textContent='Changes apply after Save. Arguments use a JSON array; environment and headers use JSON objects. Imported names already in this list are kept unchanged. Codex remote servers use HTTP transport; SSE is for Claude.';pane.append(note);
    servers.forEach((server,index)=>{
      const card=document.createElement('fieldset');card.className='mcp-card'; const legend=document.createElement('legend');legend.textContent=server.name||'New server';card.append(legend);pane.append(card);
      const read=k=>['args','env','headers'].includes(k)?JSON.stringify(server[k]??(k==='args'?[]:{}),null,2):server[k];
      const write=(k,v)=>{server[k]=v;};
      for(const spec of [['name','Server name'],['type','Transport','select','stdio,http,sse'],['enabled','Enabled','check'],['claude','Available to Claude','check'],['codex','Available to Codex','check'],['command','Command (stdio)'],['args','Arguments (JSON array)','area'],['url','URL (http / sse)'],['env','Environment (JSON object)','area'],['headers','Headers (JSON object)','area']]) {
        const input=field(spec,card,read,write);input.dataset.server=index;input.dataset.key=spec[0];delete input.dataset.setting;
        // Keep JSON text as a draft until validation, never coerce malformed input.
        if(['args','env','headers'].includes(spec[0])) input.oninput=()=>{};
      }
      button('Remove server',()=>{captureMcp();servers.splice(index,1);render();},card);
    });
  }
  function captureMcp() {
    document.querySelectorAll('[data-server]').forEach(input=>{
      const k=input.dataset.key;if(['args','env','headers'].includes(k)) {
        let value;try {value=JSON.parse(input.value);} catch {throw Error('Invalid JSON in '+k+' for '+servers[input.dataset.server].name);}
        if(k==='args' ? !Array.isArray(value)||value.some(v=>typeof v!=='string') : !value||Array.isArray(value)||typeof value!=='object'||Object.values(value).some(v=>typeof v!=='string')) throw Error(k+' must contain '+(k==='args'?'an array of strings':'an object with string values'));
        servers[input.dataset.server][k]=value;
      }
    });
  }
  function validate() {
    captureMcp();
    for(const [prefix,name] of [['deepSeek','DeepSeek'],['grok','Grok']]) if(draft[prefix+'Enabled']||draft[prefix+'Fallback']) {let url;try{url=new URL(draft[prefix+'BaseUrl']);}catch{throw Error('Enter a valid '+name+' endpoint.');}if(!['http:','https:'].includes(url.protocol))throw Error(name+' endpoint must use HTTP or HTTPS.');if(!draft[prefix+'Model']?.trim())throw Error('Enter a '+name+' model.');}
    const names=new Set();
    for(const s of servers) {
      if(!/^[\w-]+$/.test(s.name)||names.has(s.name)) throw Error('MCP names must be unique and use letters, numbers, underscores or hyphens.');names.add(s.name);
      if(s.enabled&&s.type==='stdio'&&!s.command.trim()) throw Error('Enter a command for '+s.name);
      if(s.enabled&&s.type!=='stdio') {let url;try{url=new URL(s.url);}catch{throw Error('Enter a valid URL for '+s.name);}if(!['http:','https:'].includes(url.protocol))throw Error('MCP URLs must use HTTP or HTTPS.');}
    }
    for(const specs of Object.values(groups)) for(const [key,title,type,min,max] of specs) if(type==='number'&&(!Number.isInteger(get(key))||get(key)<min||get(key)>max))throw Error(title+' must be between '+min+' and '+max+'.');
  }
  function open() {draft={retryOnError:0,retryDelaySec:5,handoffEnabled:true,resumeHistory:30,deepSeekMaxTokens:0,grokEnabled:false,grokAutoReply:false,grokFallback:false,grokFallbackOn:'any',grokApiKey:'',grokBaseUrl:'https://api.x.ai/v1',grokModel:'',grokMaxTokens:0,grokShowReasoning:false,grokSystemExtra:'',...structuredClone(current)};draft.ui={...defaults,...draft.ui};servers=structuredClone(draft.mcpServers||[]);$('modal').classList.remove('hidden');$('settingsSearch').value='';$('settingsError').textContent='';render();send({cmd:'runtimeInfo'});$('bClose').focus();}
  function close() {$('modal').classList.add('hidden');$('input').focus();}
  function init() {
    $('modal').innerHTML='<div class="dlg studio-dlg" role="dialog" aria-modal="true" aria-labelledby="settingsTitle"><div class="dlg-h"><span id="settingsTitle">XenoStudio settings</span><button class="x" id="bClose" aria-label="Close settings">✕</button></div><input id="settingsSearch" type="search" placeholder="Find a setting across all categories" aria-label="Search settings"><div class="settings-layout"><nav id="settingsTabs" role="tablist" aria-label="Settings categories"></nav><div id="settingsPane" class="dlg-b"></div></div><div id="settingsError" role="alert"></div><div class="dlg-f"><button class="btn sec" id="bSetup">Setup tools and sign-in</button><button class="btn sec" id="bCancel">Cancel</button><button class="btn" id="bSave">Save settings</button></div></div>';
    Object.keys(groups).forEach(name=>{const b=button(name,()=>{try{captureMcp();active=name;$('settingsSearch').value='';$('settingsError').textContent='';render();}catch(e){$('settingsError').textContent=e.message;}},$('settingsTabs'));b.setAttribute('role','tab');});
    $('settingsSearch').oninput=()=>{try{captureMcp();const query=$('settingsSearch').value.trim().toLowerCase();if(!query){render();return;}const pane=$('settingsPane');pane.replaceChildren();for(const [group,specs] of Object.entries(groups)){const matches=specs.filter(spec=>(group+' '+spec[1]).toLowerCase().includes(query));if(matches.length){const h=document.createElement('h2');h.textContent=group;pane.append(h);for(const spec of matches)field(spec,pane);}}if(!pane.children.length)pane.textContent='No matching settings.';}catch(e){$('settingsError').textContent=e.message;}};
    $('bSetup').onclick=()=>{close();window.studioSetup.open();};
    $('bSettings').onclick=open;$('bClose').onclick=$('bCancel').onclick=close;
    $('settingsPane').addEventListener('input',e=>{if(e.target.dataset.setting)invalidatePreview();});
    $('bSave').onclick=()=>{try{validate();draft.mcpServers=servers;send({cmd:'saveSettings',settings:draft});close();}catch(e){$('settingsError').textContent=e.message;}};
    $('modal').addEventListener('keydown',e=>{if(e.key==='Escape'){e.stopPropagation();close();}if(e.key==='Tab'){const nodes=[...$('modal').querySelectorAll('button,input,textarea,select')].filter(n=>n.getClientRects().length);const first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}});
  }
  function receive(d) {
    if(d.type==='init'&&d.promptDefaults) promptDefaults=d.promptDefaults;
    if(d.type==='promptPreview'&&previewPending&&d.id===previewPending.id&&previewPending.snapshot===JSON.stringify({...draft,mcpServers:servers})&&$('promptSystemPreview')) {
      $('promptSystemPreview').textContent=d.system||'';$('promptFooterPreview').textContent=d.footer||'';
      $('promptPreviewStatus').textContent='Preview for '+d.id+' using the current unsaved settings.';
      previewPending=null;
    }
    if(d.type==='pickedDir') {const list=(draft.workDir||'').split(/[,;|]/).map(x=>x.trim()).filter(Boolean);if(!list.some(x=>x.toLowerCase()===d.path.toLowerCase()))list.push(d.path);draft.workDir=list.join(',');if(active==='General')render();}
    if(d.type==='pickedFile'&&['claudePath','codexPath'].includes(d.key)){draft[d.key]=d.path;render();}
    if(d.type==='mcpImport') {try{captureMcp();let count=0;for(const s of d.servers||[])if(!servers.some(x=>x.name===s.name)){servers.push(s);count++;}active='MCP';render();$('settingsError').textContent=count+' servers added to draft. Save to apply.';}catch(e){$('settingsError').textContent=e.message;}}
    if(d.type==='runtime') {window.studioRuntime=JSON.stringify({claude:d.claude,codex:d.codex},null,2);if($('runtimeDetails'))$('runtimeDetails').textContent=window.studioRuntime;}
  }
  function apply(s) {for(const key of ['claudeEnabled','codexEnabled','deepSeekEnabled','grokEnabled']) if(current[key]!==s[key]){draft[key]=s[key];const input=$('setting-'+key);if(input)input.checked=!!s[key];}current=s; const ui={...defaults,...s.ui};document.documentElement.style.setProperty('--message-size',ui.fontSize+'px');document.documentElement.style.setProperty('--accent',ui.accent);
    for(const [key,value] of Object.entries({'--message-width':ui.messageWidth+'px','--message-leading':ui.lineHeight/100,'--code-size':ui.codeSize+'px'}))document.documentElement.style.setProperty(key,value);
    $('input').rows=ui.composerRows;
    for(const [cls,on] of [['no-avatars',!ui.showAvatars],['always-copy',ui.showCopy],['show-backup',ui.showBackup],['wrap-code',ui.wrapCode],['reduce-motion',ui.reduceMotion],['high-contrast',ui.highContrast]])document.body.classList.toggle(cls,on);for(const [cls,on] of [['compact',ui.density==='compact'],['no-times',!ui.timestamps],['no-tools',!ui.tools],['no-sidebar',!ui.sidebar]])document.body.classList.toggle(cls,on);return ui;}
  return {init,open,close,receive,apply};
})();
