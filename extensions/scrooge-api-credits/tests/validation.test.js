import test from 'node:test';
import assert from 'node:assert/strict';
import {normalizeReading,matches} from '../validation.js';
const reading=()=>({subject:'user-fixture',organization:'org-fixture',granted:50,used:12.5,available:37.5,
  grants:[{id:'grant-fixture',remaining:37.5,expiresAt:null}]});
test('preserves exact values and unknown expiry, drops secrets and other fields',()=>{
  const r=normalizeReading({...reading(),token:'synthetic-secret',cookie:'synthetic-cookie'});
  assert.equal(r.available,37.5); assert.equal(r.grants[0].expiresAt,null);
  assert.equal(JSON.stringify(r).includes('synthetic'),false);
});
test('rejects missing numbers, inconsistency, duplicates and nonfinite values',()=>{
  for(const delta of [{used:undefined},{available:100},{granted:Infinity},{grants:[reading().grants[0],reading().grants[0]]}])
    assert.throws(()=>normalizeReading({...reading(),...delta}));
});
test('requires the connected identity and organization on subsequent reads',()=>{
  assert.equal(matches(reading(),{subject:'user-fixture',organization:'org-fixture'}),true);
  assert.equal(matches(reading(),{subject:'user-other',organization:'org-fixture'}),false);
  assert.equal(matches(reading(),{subject:'user-fixture',organization:'org-other'}),false);
});
test('zero and overage are valid, not unknown',()=>{
  assert.equal(normalizeReading({...reading(),granted:0,used:0,available:0,grants:[]}).available,0);
  assert.equal(normalizeReading({...reading(),used:55,available:-5,grants:[]}).available,-5);
});
