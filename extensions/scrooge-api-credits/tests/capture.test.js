import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs/promises';
const source=await fs.readFile(new URL('../capture.js',import.meta.url),'utf8');
const login='https://api.openai.com/v1/dashboard/onboarding/login', grants='https://api.openai.com/v1/dashboard/billing/credit_grants';
function harness(responses){
  const events=[];
  class XHR {open(){}setRequestHeader(){}send(){}}
  const window={addEventListener(){},postMessage:e=>events.push(e),fetch:async url=>new Response(JSON.stringify(responses[url]),{status:200})};
  const context={window,XMLHttpRequest:XHR,location:{href:'https://platform.openai.com/',origin:'https://platform.openai.com'},URL,Headers,TextDecoder,Uint8Array,Date};
  vm.runInNewContext(source,context);
  return {window,events};
}
const data={total_granted:50,total_used:12.5,total_available:37.5,grants:{data:[{id:'grant-fixture',grant_amount:50,used_amount:12.5,
  effective_at:1,expires_at:null,is_scheduled:false,is_held_for_prepaid_transition:false}]}};
const identity={user:{id:'user-fixture',session:'synthetic-secret',orgs:{data:[{id:'org-fixture'}]}},secret:'synthetic-secret'};
async function settle(h){for(let i=0;i<50 && !h.events.some(e=>e.reading || e.error);i++)await new Promise(r=>setTimeout(r,5));}
test('observes page responses and exports only normalized fields',async()=>{
  const h=harness({[login]:identity,[grants]:data});
  await h.window.fetch(login,{method:'POST'});
  await h.window.fetch(grants,{headers:{'OpenAI-Organization':'org-fixture',Authorization:'synthetic-secret'}});
  await settle(h);
  const result=h.events.find(e=>e.reading)?.reading;
  assert.equal(result?.available,37.5);assert.equal(result.subject,'user-fixture');
  assert.equal(JSON.stringify(h.events).includes('synthetic-secret'),false);
});
test('does not accept a grant belonging to an unverified organization',async()=>{
  const h=harness({[login]:identity,[grants]:data});
  await h.window.fetch(login,{method:'POST'});
  await h.window.fetch(grants,{headers:{'OpenAI-Organization':'org-other'}});
  await settle(h);assert.equal(h.events.some(e=>e.reading),false);assert.equal(h.events.at(-1).error,'auth');
});
