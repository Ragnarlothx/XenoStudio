// Read-only local code previews. Host requests never execute preview content.
window.codePreview = (() => {
  const host = window.chrome?.webview, requests = new Map();
  let editable = false, attachment = null, diffEditor = null, diffMode = false, watched = null, saving = false;
  let seq = 0, version = 0, current = null, editor = null, loading = null, theme = 'dark', focusReturn;
  const panel = document.createElement('aside');
  panel.id = 'codePreview'; panel.className = 'hidden'; panel.setAttribute('aria-label','Code preview');
  panel.innerHTML = '<div id="previewGrip" role="separator" tabindex="0" aria-label="Resize code preview" aria-orientation="vertical"></div><div class="previewHead"><strong id="previewName">Code preview</strong><button id="previewClose" aria-label="Close code preview">✕</button></div><div id="previewMeta"></div><div class="previewActions"><button id="previewMode" class="btn sec sm" aria-pressed="false">Edit code</button><button id="previewAttach" class="btn sec sm">Attach code to chat</button><button id="previewCopy" class="btn sec sm">Copy</button><button id="previewSaveFile" class="btn sec sm hidden">Save file</button><button id="previewReload" class="btn sec sm hidden">Reload file</button><button id="previewSave" class="btn sec sm">Save as…</button><button id="previewEdit" class="btn sec sm hidden">Open in XenoEdit</button></div><div id="previewStatus" role="status"></div><div id="previewEditor"></div><div id="previewDiff" hidden></div><pre id="previewFallback" tabindex="0"></pre>';
  document.getElementById('main').append(panel);
  const $ = id => document.getElementById(id);
  function watch(path){if(watched&&watched!==path)host?.postMessage({cmd:'unwatchFile',path:watched});watched=path||null;if(watched)host?.postMessage({cmd:'watchFile',path:watched});}
  const launch = document.createElement('button'); launch.className='mtop'; launch.textContent='Code workspace'; launch.onclick=()=>show({name:'scratch.js',lang:'javascript',text:'// Start a draft here.\n'}); document.getElementById('bSettings').before(launch);
  const attached = document.createElement('div'); attached.id='codeAttachment'; attached.className='hidden'; document.getElementById('composer').prepend(attached);
  function text(){return current && editor && !$('previewEditor').hidden ? editor.getValue() : current?.text || '';}
  function dirty(){return !!current && text()!==current.text;}
  function allowReplace(){return !dirty() || confirm('Discard the unsaved code changes in this workspace? Save as first to keep them.');}
  function updateName(){if(current)$('previewName').textContent=current.name+(dirty()?' *':'');}
  function setMode(on){editable=on;editor?.updateOptions({readOnly:!on,domReadOnly:!on});$('previewMode').textContent=on?'Lock preview':'Edit code';$('previewMode').setAttribute('aria-pressed',String(on));status(on?'Editing a local draft. Save as to write a file.':'Read-only preview');}
  function clearAttachment(){attachment=null;attached.replaceChildren();attached.classList.add('hidden');}
  function context(){if(!attachment)return '';const runs=attachment.text.match(/`+/g)||[];const fence='`'.repeat(runs.reduce((n,x)=>Math.max(n,x.length+1),3));return '\n\nCode context - '+attachment.name+' ('+(attachment.selection?'selection':'full draft')+')\n'+fence+(attachment.lang||'')+'\n'+attachment.text+'\n'+fence;}
  $('previewMode').onclick=()=>{if(!current)return;setMode(!editable);if(editable)editor?.focus();};
  $('previewAttach').onclick=()=>{if(!current)return;const selection=editor && !$('previewEditor').hidden ? editor.getModel().getValueInRange(editor.getSelection()) : '';attachment={name:current.name,lang:current.lang,text:selection||text(),selection:!!selection};attached.replaceChildren();const label=document.createElement('span');label.textContent=attachment.name+' | '+attachment.text.split('\n').length+' lines | '+(attachment.selection?'selection':'full draft');const remove=document.createElement('button');remove.textContent='Remove';remove.onclick=clearAttachment;attached.append(label,remove);attached.classList.remove('hidden');status('Attached a snapshot to your next message. Press Send when ready.');$('input').focus();};
  const extensions = {javascript:'js',typescript:'ts',python:'py',csharp:'cs',cpp:'cpp',html:'html',css:'css',json:'json',markdown:'md',sql:'sql',powershell:'ps1',bash:'sh',xml:'xml',yaml:'yaml'};
  const aliases = {js:'javascript',ts:'typescript',py:'python',cs:'csharp','c#':'csharp',sh:'shell',bash:'shell',ps1:'powershell',yml:'yaml',md:'markdown','c++':'cpp'};
  function language(name, hint) { const ext=(name||'').split('.').pop().toLowerCase(); return aliases[hint] || hint || Object.keys(extensions).find(k=>extensions[k]===ext) || aliases[ext] || 'plaintext'; }
  function request(cmd, args={}) {
    if(!host) return Promise.reject(new Error('Open this preview inside XenoStudio.'));
    const rid='preview-'+(++seq);
    return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{requests.delete(rid);reject(new Error('The preview request timed out.'));},30000);requests.set(rid,{resolve,reject,timer});host.postMessage({cmd,rid,...args});});
  }
  host?.addEventListener('message',e=>{const d=e.data;
    if(d.type==='fileChanged'){if(current?.path && norm(d.path)===norm(current.path)&&!saving){current.externalChanged=true;status(d.deleted?'File was deleted on disk. Your draft is still available.':'File changed on disk.');if(!d.deleted&&(!dirty()||confirm('This file changed on disk. Reload and discard your unsaved edits?')))file(current.path,true);}return;}
    if(d.type==='xenoEditInfo'){$('previewEdit').classList.toggle('hidden',!(d.exe||d.data?.exe));return;}
    const q=requests.get(d.rid);if(!q)return;clearTimeout(q.timer);requests.delete(d.rid);if(d.cancelled)q.resolve(null);else if(d.ok)q.resolve(d.data);else q.reject(new Error(d.error||'Preview request failed.'));
  });
  const norm=p=>(p||'').replace(/\\/g,'/').toLowerCase();
  function status(text){$('previewStatus').textContent=text||'';}
  function applyTheme(value){theme=value||'dark';if(!window.monaco)return;
    monaco.editor.defineTheme('studio-cyberpunk',{base:'vs-dark',inherit:true,rules:[],colors:{'editor.background':'#0b0618','editor.foreground':'#eee8fa','editorLineNumber.foreground':'#a391bd'}});
    monaco.editor.defineTheme('studio-hacker',{base:'vs-dark',inherit:true,rules:[{token:'comment',foreground:'71aa7a'},{token:'keyword',foreground:'33ff66'}],colors:{'editor.background':'#020602','editor.foreground':'#b7efbe'}});
    monaco.editor.setTheme(theme==='light'?'vs':theme==='cyberpunk'?'studio-cyberpunk':theme==='hacker'?'studio-hacker':'vs-dark');
    editor?.updateOptions({fontFamily:theme==='hacker'?'Cascadia Mono, Consolas, monospace':'Cascadia Code, Consolas, monospace'});
  }
  function loadEditor(){if(loading)return loading;loading=new Promise((resolve,reject)=>{
    const script=document.createElement('script');script.src='lib/vs/loader.js';script.onerror=()=>reject(new Error('Editor unavailable; plain text preview remains available.'));script.onload=()=>{
      window.MonacoEnvironment={getWorkerUrl:()=>URL.createObjectURL(new Blob(["self.MonacoEnvironment={baseUrl:'"+new URL('lib/',location.href).href+"'};importScripts('"+new URL('lib/vs/base/worker/workerMain.js',location.href).href+"');"],{type:'text/javascript'}))};
      window.require.config({paths:{vs:new URL('lib/vs',location.href).href}});window.require(['vs/editor/editor.main'],resolve,reject);
    };document.head.append(script);
  });return loading;}
  async function show(data, accepted=false){if(!accepted&&!allowReplace())return;const token=++version;current=data;diffMode=false;$('previewDiff').hidden=true;watch(data.path);$('previewSaveFile').classList.toggle('hidden',!data.path);$('previewReload').classList.toggle('hidden',!data.path);$('previewSaveFile').disabled=!!data.truncated;focusReturn=document.activeElement;panel.classList.remove('hidden');
    $('previewName').textContent=data.name||'Code preview';$('previewName').title=data.path||data.name||'';
    $('previewMeta').textContent=(data.lang||language(data.name))+' · '+data.text.split('\n').length+' lines'+(data.truncated?' · truncated at 2 MB':'');
    $('previewFallback').textContent=data.text;$('previewFallback').hidden=false;$('previewEditor').hidden=true;status(data.truncated?'Preview is truncated. Open in XenoEdit to inspect the complete file.':'Read-only preview');
    $('previewReload').disabled=false;$('previewMode').disabled=true;$('previewAttach').disabled=false;$('previewEdit').disabled=false; $('previewCopy').disabled=false; $('previewSave').disabled=!!data.truncated;host?.postMessage({cmd:'xenoEditInfo'});$('previewClose').focus();
    try{await loadEditor();if(token!==version)return;if(!editor){editor=monaco.editor.create($('previewEditor'),{readOnly:true,domReadOnly:true,automaticLayout:true,minimap:{enabled:true},scrollBeyondLastLine:false,fontSize:13});editor.onDidChangeModelContent(updateName);}
      const old=editor.getModel();const model=monaco.editor.createModel(data.text,data.lang||language(data.name));editor.setModel(model);old?.dispose();applyTheme(theme);$('previewEditor').hidden=false;$('previewFallback').hidden=true;editor.layout();$('previewMode').disabled=!!data.truncated;setMode(false);
    }catch{if(token===version)status('Editor unavailable. Read-only plain text is shown.');}
  }
  async function file(path,accepted=false){if(!accepted&&!allowReplace())return;const token=++version;status('Loading file…');try{const d=await request('previewFile',{path});if(token===version)await show({...d,lang:language(d.name)},true);}catch(e){if(token===version){panel.classList.remove('hidden');status(e.message);}}}
  function close(){if(!allowReplace())return;++version;panel.classList.add('hidden');watch(null);focusReturn?.focus();}
  $('previewClose').onclick=close;panel.addEventListener('keydown',e=>{if(e.key==='Escape'){e.stopPropagation();close();}});
  $('previewCopy').onclick=()=>current&&navigator.clipboard.writeText(text()).then(()=>status('Copied.'),()=>status('Copy failed.'));
  $('previewSave').onclick=async()=>{if(!current)return;try{const draft=current,snapshot=text();const d=await request('saveCode',{text:snapshot,name:draft.name});if(d){if(current===draft){current.text=snapshot;current.path=d.path;updateName();}status('Saved to '+d.path);}}catch(e){status(e.message);}};
  $('previewEdit').onclick=async()=>{if(!current)return;try{await request('openInXenoEdit',current.path&&!dirty()?{path:current.path}:{text:text(),name:current.name});status('Opened in XenoEdit.');}catch(e){status(e.message);}};
  $('previewReload').onclick=()=>current?.path&&file(current.path);
  $('previewSaveFile').onclick=async()=>{if(!current?.path||current.truncated)return;if(current.externalChanged&&!confirm('The file changed on disk. Overwrite it with this draft?'))return;const draft=current,snapshot=text();saving=true;try{const d=await request('saveFile',{path:draft.path,text:snapshot});if(current===draft){current.text=snapshot;current.externalChanged=false;updateName();status('Saved file (+'+d.added+' -'+d.removed+').');}}catch(e){status(e.message);}finally{saving=false;}};
  async function diff(item){if(!allowReplace())return;const token=++version;try{const d=await request('fileDiff',{snap:item.snap});if(token!==version)return;
    await show({name:item.path.split(/[\\/]/).pop(),text:d.after||'',lang:language(item.path)},true);if(version!==token+1)return;
    if(d.beforeUnknown){status('Before not captured. Showing the recorded result only.');return;}await loadEditor();diffMode=true;watch(null);$('previewEditor').hidden=true;$('previewFallback').hidden=true;$('previewDiff').hidden=false;
    for(const id of ['previewMode','previewSaveFile','previewReload','previewSave','previewAttach','previewEdit'])$(id).disabled=true;
    if(!diffEditor)diffEditor=monaco.editor.createDiffEditor($('previewDiff'),{readOnly:true,originalEditable:false,automaticLayout:true,renderSideBySide:false,fontSize:13});
    const old=diffEditor.getModel();diffEditor.setModel({original:monaco.editor.createModel(d.before||'',language(item.path)),modified:monaco.editor.createModel(d.after||'',language(item.path))});old?.original.dispose();old?.modified.dispose();diffEditor.layout();status('Recorded AI change - before and after.');
  }catch(e){panel.classList.remove('hidden');status(e.message);}}
  function cards(container,files){for(const item of files||[]){const b=document.createElement('button');b.className='changeCard';b.textContent=(item.who||'AI')+' changed '+item.path+'  +'+item.added+' -'+item.removed+(item.created?' (created)':item.deleted?' (deleted)':'')+(item.beforeUnknown?' - before not captured':'');b.onclick=()=>diff(item);container.append(b);}}
  function resize(width){panel.style.width=Math.max(280,Math.min(width,innerWidth-240))+'px';editor?.layout();}
  const grip=$('previewGrip');grip.onpointerdown=e=>{grip.setPointerCapture(e.pointerId);};grip.onpointermove=e=>{if(grip.hasPointerCapture(e.pointerId))resize(innerWidth-e.clientX);};grip.onkeydown=e=>{if(['ArrowLeft','ArrowRight'].includes(e.key)){e.preventDefault();resize(panel.getBoundingClientRect().width+(e.key==='ArrowLeft'?24:-24));}};
  function isPath(text){return /^(?:[A-Za-z]:[\\/])?(?:[\w .@()-]+[\\/])*[\w .@()-]+\.[a-z0-9]{1,12}(?::\d+(?::\d+)?)?$/i.test(text)&&!/^https?:/i.test(text);}
  function chip(node,path){const b=document.createElement('button');b.className='fileChip';b.textContent=node.textContent;b.title='Preview '+path;b.onclick=()=>file(path.replace(/:\d+(?::\d+)?$/,''));node.replaceWith(b);}
  function enhance(txt,tools){
    for(const pre of txt.querySelectorAll('pre')){const code=pre.querySelector('code');if(!code||code.textContent.split('\n').length<=5)continue;
      const text=code.textContent,hint=pre.querySelector('.lang')?.textContent||'',lang=language('',hint),name=pre.dataset.name||('snippet.'+(extensions[lang]||'txt'));
      const tile=document.createElement('div');tile.className='codeTile';const open=document.createElement('button');open.className='codeTileOpen';open.textContent=name+' · '+(hint||'code')+' · '+text.split('\n').length+' lines';open.title='Open read-only code preview';open.onclick=()=>show({text,lang,name});const copy=document.createElement('button');copy.className='btn sec sm';copy.textContent='Copy';copy.onclick=()=>navigator.clipboard.writeText(text).catch(()=>status('Copy failed.'));tile.append(open,copy);pre.replaceWith(tile);
    }
    for(const node of txt.querySelectorAll('[data-preview-path]'))node.onclick=()=>file(node.dataset.previewPath);
    for(const node of txt.querySelectorAll('code'))if(!node.closest('pre')&&isPath(node.textContent.trim()))chip(node,node.textContent.trim());
    for(const node of tools.querySelectorAll('.tool')){const m=node.textContent.match(/(?:edit|write|read|update|patch)\s+(.+\.[a-z0-9]{1,12})$/i);if(m&&isPath(m[1]))chip(node,m[1]);}
  }
  return {enhance,theme:applyTheme,file,context,clearAttachment,cards};
})();
