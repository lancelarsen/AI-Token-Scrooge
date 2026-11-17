import test from 'node:test';
import assert from 'node:assert/strict';
const callbacks={},sent=[],tabs=new Map(),removed=[],reloaded=[];
let nextTab=1;
const event=name=>({addListener:fn=>{callbacks[name]=fn;}});
globalThis.chrome={
  runtime:{id:'fixture-extension',getURL:p=>'chrome-extension://fixture-extension/'+p,
    connectNative:()=>({postMessage:m=>sent.push(m),onMessage:event('native'),onDisconnect:event('disconnect')}),
    onMessage:event('message'),onStartup:event('startup'),onInstalled:event('installed')},
  tabs:{create:async options=>{const tab={id:nextTab++,...options};tabs.set(tab.id,tab);return tab;},
    update:async(id,options)=>Object.assign(tabs.get(id),options),get:async id=>tabs.get(id),
    remove:async id=>{removed.push(id);tabs.delete(id);},reload:async id=>reloaded.push(id),onRemoved:event('removed')},
  storage:{session:{get:async()=>({}),set:async()=>{}}},alarms:{create:async()=>{},onAlarm:event('alarm')}
};
await (await import('../worker.js')).initialized;
const tick=()=>new Promise(r=>setImmediate(r));
const popup={id:'fixture-extension',url:chrome.runtime.getURL('popup.html')};
const sender=(tab,documentId='doc-1')=>({id:'fixture-extension',frameId:0,origin:'https://platform.openai.com',
  url:'https://platform.openai.com/settings/organization/billing/credit-grants',tab:{id:tab},documentId});
const reading=()=>({subject:'user-fixture',organization:'org-fixture',granted:50,used:10,available:40,grants:[]});
function message(m,s){let response;callbacks.message(m,s,r=>response=r);return response;}
test('interactive confirmation reloads and verifies before sending; never relays added secrets',async()=>{
  const id='1'.repeat(32);callbacks.native({type:'read',id,interactive:true,subject:null,organization:null});await tick();
  const tab=nextTab-1;
  assert.equal(message({type:'ready'},sender(tab)).armed,true);
  message({type:'capture',data:{reading:{...reading(),token:'synthetic-secret'}}},sender(tab));
  assert.equal(sent.length,0);
  // A content script cannot approve a connection.
  message({type:'accept',id},sender(tab));assert.equal(reloaded.length,0);
  message({type:'accept',id},popup);await tick();assert.equal(reloaded.at(-1),tab);
  message({type:'ready'},sender(tab,'doc-2'));
  message({type:'capture',data:{reading:reading()}},sender(tab,'doc-1'));assert.equal(sent.length,0);
  message({type:'capture',data:{reading:{...reading(),token:'synthetic-secret'}}},sender(tab,'doc-2'));await tick();
  assert.equal(sent.at(-1).id,id);assert.equal(sent.at(-1).reading.available,40);
  assert.equal(JSON.stringify(sent).includes('synthetic-secret'),false);
});
test('background rejects a different organization and cleans up its own tab',async()=>{
  const id='2'.repeat(32);callbacks.native({type:'read',id,interactive:false,subject:'user-fixture',organization:'org-other'});await tick();
  const tab=nextTab-1;message({type:'ready'},sender(tab));
  message({type:'capture',data:{reading:reading()}},sender(tab));await tick();
  assert.equal(sent.at(-1).error,'auth');assert.ok(removed.includes(tab));
});
test('cancel discards late readings and unsolicited pages cannot write data',async()=>{
  const id='3'.repeat(32);callbacks.native({type:'read',id,interactive:false});await tick();
  const tab=nextTab-1;message({type:'ready'},sender(tab));
  callbacks.native({type:'cancel',id});await tick();const count=sent.length;
  message({type:'capture',data:{reading:reading()}},sender(tab));
  assert.equal(message({type:'ready'},sender(999)).armed,false);
  assert.equal(sent.length,count);assert.ok(removed.includes(tab));
});
