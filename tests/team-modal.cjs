// Executes the actual staged team handlers with a minimal DOM adapter and mocked APIs.
// No real requests or membership writes. Browser layout is not simulated.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../FrontendIntegration/TeamModal/app/js/settings.js'), 'utf8');
const deferred = () => { let resolve, reject; const promise = new Promise((a,b) => { resolve=a; reject=b; }); return {promise,resolve,reject}; };
const user = id => ({idUser:id,fullName:`User ${id}`,username:`u${id}@example.test`,isActive:true});
function screen() {
  const nodes = new Map(), handlers = new Map(), requests = [];
  const node = key => { if (!nodes.has(key)) nodes.set(key,{dataset:{},attrs:{},props:{},classes:new Set(),html:'',text:''}); return nodes.get(key); };
  const tabs = ['team','add'].map(t => Object.assign(node('tab-'+t),{dataset:{memberTab:t}}));
  const panels = ['team','add'].map(t => Object.assign(node('panel-'+t),{dataset:{memberPanel:t}}));
  function $(value) {
    let items = typeof value !== 'string' ? [value] : value === '#teamMembersTabs [data-member-tab]' ? tabs
      : value === '#teamMembersModal [data-member-panel]' ? panels : value.split(',').map(k => node(k.trim()));
    const result = {
      each(fn) { items.forEach(n => fn.call(n)); return result; },
      on(event, selector, fn) { handlers.set(`${value}:${event}`,fn || selector); return result; },
      prop(k,v) {items.forEach(n => n.props[k]=v);return result;},
      attr(k,v) {items.forEach(n => n.attrs[k]=v);return result;},
      text(v) {items.forEach(n => n.text=v);return result;},
      html(v) {items.forEach(n => n.html=v);return result;},
      toggleClass(k,on) {items.forEach(n => on?n.classes.add(k):n.classes.delete(k));return result;},
      addClass(k) {return result.toggleClass(k,true);}, removeClass(k) {return result.toggleClass(k,false);},
      trigger() {return result;}
    }; return result;
  }
  const state = {users:[user(1),user(2),user(3)],members:[user(1)],write:true,view:true,posts:0,renders:0};
  const context = {$,document:{activeElement:{isConnected:true}},
    appAccess:{can:(_,action) => action==='view'?state.view:state.write,applyControls(){}},
    initials: n=>n.slice(0,2),escapeHtml:n=>String(n).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;'),
    renderTeams:async()=>{state.renders++;},
    apiFetch:async(url,options)=>{
      requests.push({url,options});
      if(options?.method==='POST'){state.posts++;return state.save?state.save.promise:{};}
      if(url==='/api/user/list')return state.users;
      if(state.load)return state.load.promise;
      return state.members;
    }
  };
  const start = source.slice(source.indexOf('  let currentTeamForMembers'),source.indexOf('  async function getDepartments'));
  const functions = source.slice(source.indexOf('  function selectTeamMembersTab'),source.indexOf('  $("#addDepartmentBtn")'));
  const save = source.slice(source.indexOf('  $("#teamMembersSaveBtn").on'),source.indexOf('  $(document).on("click", "[data-close-department-modal]"'));
  vm.runInNewContext('let teamsCache=null;\n'+start+functions+save+'\nthis.open=openTeamMembersModal;this.close=closeTeamMembersModal;',context);
  return {state,node,tabs,panels,requests,open:context.open,close:context.close,
    save:()=>handlers.get('#teamMembersSaveBtn:click')(),
    change:(id,checked)=>{const input={value:String(id),checked};handlers.get('#teamMembersModal:change').call(input);return input;},
    tab:t=>handlers.get('#teamMembersTabs:click').call({dataset:{memberTab:t}})};
}
const tests=[];
const test=(name,fn)=>tests.push([name,fn]);
test('Separates existing and available members; selections survive tab switches',async()=>{
  const s=screen();await s.open(1,'A');
  assert(s.node('#teamMemberList').html.includes('value="1"'));assert(!s.node('#teamMemberList').html.includes('value="2"'));
  assert(s.node('#addMemberList').html.includes('value="2"'));assert(s.node('#teamMembersSaveBtn').props.disabled);
  s.tab('add');assert(s.panels[1].classes.has('active'));assert(!s.panels[0].classes.has('active'));
  s.change(2,true);s.tab('team');s.change(1,false);s.tab('add');await s.save();
  assert.deepEqual(Array.from(s.requests.find(r=>r.options)?.options.body.userIds),[2]);assert.equal(s.state.renders,1);
});
test('Loading and failed loading never submit an empty membership',async()=>{
  const s=screen();s.state.load=deferred();const opening=s.open(1,'A');await s.save();assert.equal(s.state.posts,0);
  s.state.load.reject(new Error('offline'));await opening;await s.save();assert.equal(s.state.posts,0);
  assert(s.node('#teamMembersSaveBtn').props.disabled);assert.equal(s.node('#teamMembersFormError').text,'offline');
});
test('Malformed response fails closed',async()=>{const s=screen();s.state.users=null;await s.open(1,'A');await s.save();assert.equal(s.state.posts,0);assert(s.node('#teamMembersSaveBtn').props.disabled);});
test('Existing members omitted from user list are preserved',async()=>{const s=screen();s.state.members=[user(9)];await s.open(1,'A');s.change(2,true);await s.save();assert.deepEqual(Array.from(s.requests.find(r=>r.options).options.body.userIds),[9,2]);});
test('Empty team supports adding; removing all is possible only after successful load',async()=>{
  const s=screen();s.state.members=[];await s.open(1,'A');assert(s.node('#teamMemberList').html.includes('no members'));s.change(3,true);await s.save();assert.equal(s.state.posts,1);
  const t=screen();await t.open(1,'A');t.change(1,false);await t.save();assert.equal(t.requests.find(r=>r.options).options.body.userIds.length,0);
});
test('Cancel discards unsaved selection',async()=>{const s=screen();await s.open(1,'A');s.change(2,true);s.close();await s.open(1,'A');await s.save();assert.equal(s.state.posts,0);assert(s.node('#teamMembersSaveBtn').props.disabled);});
test('Stale load cannot overwrite another team',async()=>{
  const s=screen();const old=deferred();s.state.load=old;const first=s.open(1,'A');s.state.load=null;s.state.members=[user(2)];await s.open(2,'B');old.resolve([user(1)]);await first;
  assert(s.node('#teamMemberList').html.includes('value="2"'));assert(!s.node('#teamMemberList').html.includes('value="1"'));
});
test('Closing during load invalidates response',async()=>{const s=screen();s.state.load=deferred();const opening=s.open(1,'A');s.close();s.state.load.resolve([user(1)]);await opening;await s.save();assert.equal(s.state.posts,0);assert.equal(s.node('#teamMembersModal').attrs['aria-hidden'],'true');});
test('Read-only users cannot alter or save membership',async()=>{const s=screen();s.state.write=false;await s.open(1,'A');assert.equal(s.change(2,true).checked,false);await s.save();assert.equal(s.state.posts,0);assert(s.node('#teamMemberList').html.includes('disabled'));});
test('Save failure preserves selection and permits retry',async()=>{
  const s=screen();await s.open(1,'A');s.change(2,true);s.state.save=deferred();const saving=s.save();s.state.save.reject(new Error('save failed'));await saving;
  assert.equal(s.node('#teamMembersFormError').text,'save failed');assert.equal(s.node('#teamMembersSaveBtn').props.disabled,false);
  s.state.save=null;await s.save();assert.equal(s.state.posts,2);assert.deepEqual(Array.from(s.requests.filter(r=>r.options).at(-1).options.body.userIds),[1,2]);
});
test('Duplicate save, close, reopen and edits are blocked while saving',async()=>{
  const s=screen();await s.open(1,'A');s.change(2,true);s.state.save=deferred();const saving=s.save();await s.save();s.close();await s.open(2,'B');assert.equal(s.change(3,true).checked,false);
  assert.equal(s.state.posts,1);assert.equal(s.node('#teamMembersModal').attrs['aria-hidden'],'false');s.state.save.resolve({});await saving;assert.equal(s.node('#teamMembersModal').attrs['aria-hidden'],'true');
});
test('Unknown user IDs and unchanged selections do not write',async()=>{const s=screen();await s.open(1,'A');assert.equal(s.change(999,true).checked,false);s.change(2,true);s.change(2,false);await s.save();assert.equal(s.state.posts,0);});
(async()=>{for(const [name,fn] of tests){await fn();console.log('PASS '+name);}console.log(`${tests.length} simulated team scenarios passed. Live browser/SQL verification is separate.`);})().catch(e=>{console.error(e);process.exitCode=1;});
