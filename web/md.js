// Small, safe markdown renderer for chat messages (no external deps, everything HTML-escaped first).
(function () {
  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

  function inline(s) {
    const slots = [];
    const keep = html => '\u0000' + (slots.push(html) - 1) + '\u0000';
    s = s.replace(/`([^`\n]+)`/g, (_, c) => keep('<code>' + esc(c) + '</code>'));
    s = esc(s);
    s = s.replace(/\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)/g, (_, t, u) => keep('<a href="' + u + '">' + t + '</a>'));
    s = s.replace(/(^|[\s(])(https?:\/\/[^\s<)]+[^\s<).,;:!?'"])/g, (_, p, u) => p + keep('<a href="' + u + '">' + u + '</a>'));
    s = s.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
    s = s.replace(/__([^_\n]+)__/g, '<strong>$1</strong>');
    s = s.replace(/(^|[^\w*])\*([^*\n]+)\*(?!\w)/g, '$1<em>$2</em>');
    s = s.replace(/(^|[^\w])_([^_\n]+)_(?!\w)/g, '$1<em>$2</em>');
    s = s.replace(/~~([^~\n]+)~~/g, '<del>$1</del>');
    s = s.replace(/(^|[^\w@])@(claude|codex|xeno)\b/gi, (_, p, w) => p + '<span class="mention ' + w.toLowerCase() + '">@' + w + '</span>');
    return s.replace(/\u0000(\d+)\u0000/g, (_, i) => slots[+i]);
  }

  const isTableSep = l => /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(l);
  const cells = l => l.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());

  function render(src) {
    const lines = (src || '').replace(/\r\n?/g, '\n').split('\n');
    let out = '', i = 0, para = [];
    const flush = () => { if (para.length) { out += '<p>' + para.map(inline).join('<br>') + '</p>'; para = []; } };

    while (i < lines.length) {
      const l = lines[i];
      let m;
      if ((m = l.match(/^\s*(```+|~~~+)\s*([\w#+.-]*)/))) {
        flush();
        const fence = m[1], lang = m[2];
        const code = [];
        i++;
        while (i < lines.length && !lines[i].trim().startsWith(fence)) code.push(lines[i++]);
        i++;
        out += '<pre>' + (lang ? '<span class="lang">' + esc(lang) + '</span>' : '') + '<button class="cp">Copy</button><code>' + esc(code.join('\n')) + '</code></pre>';
        continue;
      }
      if (!l.trim()) { flush(); i++; continue; }
      if ((m = l.match(/^(#{1,4})\s+(.*)$/))) { flush(); out += '<h' + m[1].length + '>' + inline(m[2]) + '</h' + m[1].length + '>'; i++; continue; }
      if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(l)) { flush(); out += '<hr>'; i++; continue; }
      if (l.includes('|') && i + 1 < lines.length && isTableSep(lines[i + 1])) {
        flush();
        const head = cells(l);
        i += 2;
        let t = '<table><thead><tr>' + head.map(c => '<th>' + inline(c) + '</th>').join('') + '</tr></thead><tbody>';
        while (i < lines.length && lines[i].includes('|') && lines[i].trim()) {
          t += '<tr>' + cells(lines[i]).map(c => '<td>' + inline(c) + '</td>').join('') + '</tr>';
          i++;
        }
        out += t + '</tbody></table>';
        continue;
      }
      if (/^\s*>/.test(l)) {
        flush();
        const q = [];
        while (i < lines.length && /^\s*>/.test(lines[i])) q.push(lines[i++].replace(/^\s*>\s?/, ''));
        out += '<blockquote>' + render(q.join('\n')) + '</blockquote>';
        continue;
      }
      if ((m = l.match(/^(\s*)([-*+]|\d+[.)])\s+/))) {
        flush();
        const ordered = /\d/.test(m[2]);
        const items = [];
        while (i < lines.length) {
          const li = lines[i].match(/^(\s*)([-*+]|\d+[.)])\s+(.*)$/);
          if (li) { items.push((li[1].length >= 2 ? '  ' : '') + li[3]); i++; }
          else if (lines[i].trim() && /^\s{2,}/.test(lines[i]) && items.length) { items[items.length - 1] += ' ' + lines[i].trim(); i++; }
          else break;
        }
        out += (ordered ? '<ol>' : '<ul>') + items.map(x => '<li>' + inline(x) + '</li>').join('') + (ordered ? '</ol>' : '</ul>');
        continue;
      }
      para.push(l);
      i++;
    }
    flush();
    return out;
  }

  window.md = render;
})();
