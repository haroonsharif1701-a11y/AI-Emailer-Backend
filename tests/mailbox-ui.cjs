const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const { webcrypto } = require('node:crypto');

async function screen(script, providers) {
  const elements = [], selectors = new Map(), ready = [], requests = [], storage = new Map();
  const element = () => { const node = { props: {}, attrs: {}, events: {}, kids: [], value: '', text: '', checkValidity: () => true }; elements.push(node); return node; };
  function wrap(nodes) {
    const result = { length: nodes.length, ...Object.fromEntries(nodes.map((node, index) => [index, node])) };
    result.prop = (key, value) => { if (value === undefined) return nodes[0]?.props[key]; nodes.forEach(node => node.props[key] = value); return result; };
    result.attr = (key, value) => { nodes.forEach(node => Object.assign(node.attrs, typeof key === 'object' ? key : { [key]: value })); return result; };
    result.text = value => { if (value === undefined) return nodes[0]?.text; nodes.forEach(node => node.text = value); return result; };
    result.val = value => { if (value === undefined) return nodes[0]?.value; nodes.forEach(node => node.value = value); return result; };
    result.on = (event, callback) => { nodes.forEach(node => node.events[event] = callback); return result; };
    result.appendTo = target => { const parent = typeof target === 'string' ? $(target)[0] : target[0] || target; parent.kids.push(...nodes); return result; };
    result.empty = () => { nodes.forEach(node => { node.kids = []; node.text = ''; }); return result; };
    result.children = () => wrap(nodes.flatMap(node => node.kids));
    result.data = key => nodes[0]?.attrs['data-' + key];
    for (const method of ['css', 'remove', 'addClass', 'removeClass']) result[method] = () => result;
    result.html = () => { throw new Error('Unexpected HTML insertion'); };
    return result;
  }
  function $(value, attributes) {
    if (typeof value === 'function') { ready.push(Promise.resolve().then(value)); return; }
    if (typeof value !== 'string') return wrap([value]);
    if (value.startsWith('<')) {
      const node = element();
      Object.assign(node.attrs, attributes || {});
      node.text = attributes?.text || '';
      return wrap([node]);
    }
    const nodes = value.split(',').map(selector => {
      selector = selector.trim();
      if (!selectors.has(selector)) { const node = element(); node.id = selector.slice(1); selectors.set(selector, node); }
      return selectors.get(selector);
    });
    return wrap(nodes);
  }
  const mailbox = { id: 'm1', subject: '<img src=x onerror=alert(1)>', from: 'Sender <sender@example.com>', to: 'test@example.com',
    bodyText: '<script>alert(1)</script>', snippet: 'Preview', labels: ['INBOX', 'UNREAD'], attachments: [] };
  const context = {
    $, appAccess: { ready: Promise.resolve(true), can: () => true }, document: { hidden: false, body: element() },
    API_BASE_URL: 'https://localhost:5080', crypto: webcrypto, TextEncoder, Uint8Array, URL, URLSearchParams,
    btoa: value => Buffer.from(value, 'binary').toString('base64'), setInterval: () => 1, setTimeout: () => 1,
    sessionStorage: { setItem: (key, value) => storage.set(key, value) }, alert: () => {}, confirm: () => true,
    window: { location: {}, open: () => ({ location: '', close() {} }) },
    apiFetch: async (url, options = {}) => {
      requests.push({ url, ...options });
      if (url === '/api/auth/providers') return providers;
      if (url === '/api/mailbox/status') return { connected: true, email: 'test@example.com' };
      if (url.startsWith('/api/mailbox/messages?')) return { messages: [mailbox], nextPageToken: null };
      if (url === '/api/mailbox/messages/m1') return mailbox;
      if (url.endsWith('/reply-draft')) return { draft: 'Reviewed draft' };
      if (url.endsWith('/summary')) return { summary: 'Summary', priority: 'Normal', actionItems: [] };
      return {};
    }
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../FrontendIntegration/app/js', script), 'utf8'), context);
  await Promise.all(ready);
  await new Promise(resolve => setImmediate(resolve));
  return { selectors, elements, requests, storage, context };
}

(async () => {
  const auth = await screen('auth.js', { gmail: true, outlook: false });
  assert.equal(auth.selectors.get('#loginMicrosoft').props.hidden, true);
  assert.equal(auth.selectors.get('#loginGoogle').props.disabled, false);
  const google = auth.selectors.get('#loginGoogle');
  await google.events.click.call(google);
  const redirect = new URL(auth.context.window.location.href);
  assert.equal(redirect.searchParams.get('provider'), 'Google');
  assert.equal(redirect.searchParams.get('challenge').length, 43);
  assert.equal(auth.storage.get('ai-emailer-oauth-verifier').length, 43);
  console.log('PASS provider visibility and browser-bound login challenge');
  const off = await screen('inbox.js', { gmail: false, outlook: false });
  assert.equal(off.requests.some(request => request.url === '/api/mailbox/status'), false);
  console.log('PASS disabled mailbox does not fetch messages');
  const inbox = await screen('inbox.js', { gmail: true, outlook: false });
  assert(inbox.requests.some(request => request.url.startsWith('/api/mailbox/messages?')));
  const message = inbox.selectors.get('#inboxItems').kids[0];
  await message.events.click.call(message);
  assert(inbox.elements.some(node => node.text === '<script>alert(1)</script>'));
  assert(inbox.elements.some(node => node.attrs.class === 'mail-header'));
  assert(inbox.elements.some(node => node.attrs.class === 'mail-toolbar' && node.attrs['aria-label'] === 'Message actions'));
  assert(inbox.elements.some(node => node.attrs.class === 'mail-body' && node.text === '<script>alert(1)</script>'));
  assert(inbox.elements.some(node => node.attrs.for === 'mailReplyTo'));
  assert(inbox.elements.some(node => node.attrs.for === 'mailReplyBody'));
  assert.equal(inbox.requests.some(request => request.url.endsWith('/messages/send')), false);
  const draft = inbox.elements.find(node => node.text === 'Generate reply draft');
  await draft.events.click.call(draft);
  assert.equal(inbox.requests.some(request => request.url.endsWith('/messages/send')), false);
  assert(inbox.elements.some(node => node.value === 'Reviewed draft'));
  const send = inbox.elements.find(node => node.text === 'Send reply');
  await send.events.click.call(send);
  const sent = inbox.requests.find(request => request.url.endsWith('/messages/send'));
  assert.equal(sent.body.replyToMessageId, 'm1');
  assert.equal(sent.body.body, 'Reviewed draft');
  console.log('PASS live-message binding, text-only rendering, draft review and explicit send');
  console.log('3 simulated UI scenarios passed; browser layout and live OAuth are not covered.');
})().catch(error => { console.error(error); process.exitCode = 1; });
