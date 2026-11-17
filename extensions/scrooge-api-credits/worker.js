import {PAGE,normalizeReading,matches} from './validation.js';
const jobs = new Map();
let port = null, status = 'Open Scrooge to Connect', connecting = false;
async function closeJob(job) {
  jobs.delete(job.id);
  if (!job.interactive && job.tabId !== undefined) {
    const tab = await chrome.tabs.get(job.tabId).catch(() => null);
    if (tab && !tab.active) await chrome.tabs.remove(job.tabId).catch(() => {});
  }
  await chrome.storage.session.set({ownedTabs:[...jobs.values()].filter(j => !j.interactive).map(j => j.tabId)});
}
async function finish(job, payload) {
  if (jobs.get(job.id) !== job) return;
  port?.postMessage({type:'result',id:job.id,...payload});
  status = payload.reading ? 'Balance Sent to Scrooge' : 'Billing Read Needs Attention';
  await closeJob(job);
}
async function read(message) {
  if (!/^[a-f0-9]{32}$/.test(message.id) || typeof message.interactive !== 'boolean' || jobs.size >= 16) return;
  if (jobs.has(message.id)) return;
  const job = {...message,candidate:null,documentId:null,verifying:false};
  jobs.set(job.id,job);
  try {
    // Create first so the request is bound to the tab before Platform starts loading.
    const tab = await chrome.tabs.create({url:'about:blank',active:job.interactive});
    job.tabId = tab.id;
    if (jobs.get(job.id) !== job) { await chrome.tabs.remove(tab.id); return; }
    await chrome.storage.session.set({ownedTabs:[...jobs.values()].filter(j => !j.interactive).map(j => j.tabId)});
    await chrome.tabs.update(tab.id,{url:PAGE});
    status = job.interactive ? 'Select Your Organization, Then Open This Extension' : 'Reading API Credits';
  } catch { await finish(job,{error:'offline'}); }
}
function connect() {
  if (port || connecting) return;
  connecting = true;
  try {
    const current = chrome.runtime.connectNative('com.aitokenscrooge.billing');
    port = current; status = 'Connected to Scrooge';
    current.onMessage.addListener(message => {
      if (message.type === 'read') void read(message);
      if (message.type === 'cancel' && jobs.has(message.id)) void closeJob(jobs.get(message.id));
    });
    current.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      if (port !== current) return;
      port = null; status = 'Open Scrooge and Check the Chrome Bridge Installation';
      for (const job of [...jobs.values()]) void closeJob(job);
    });
  } finally { connecting = false; }
}
chrome.runtime.onMessage.addListener((message,sender,respond) => {
  if (sender.id !== chrome.runtime.id) return;
  // Popup commands are never accepted from a web page/content script.
  if (!sender.tab && sender.url === chrome.runtime.getURL('popup.html')) {
    if (message.type === 'status') {
      connect();
      respond({status,jobs:[...jobs.values()].filter(j=>j.interactive).map(j=>({id:j.id,candidate:j.candidate,verifying:j.verifying}))});
    } else if (message.type === 'accept') {
      const job = jobs.get(message.id);
      if (job?.interactive && job.candidate && !job.verifying) {
        job.subject = job.candidate.subject; job.organization = job.candidate.organization;
        job.verifying = true; job.candidate = null; job.documentId = null;
        void chrome.tabs.reload(job.tabId).catch(()=>finish(job,{error:'offline'}));
        respond({ok:true});
      }
    }
    return;
  }
  if (sender.frameId !== 0 || sender.origin !== 'https://platform.openai.com' ||
      !sender.url?.startsWith('https://platform.openai.com/')) return;
  const job = [...jobs.values()].find(j=>j.tabId === sender.tab?.id);
  if (!job) { respond({armed:false}); return; }
  if (message.type === 'ready') {
    job.documentId = sender.documentId; job.candidate = null;
    respond({armed:true}); return;
  }
  if (message.type !== 'capture' || sender.documentId !== job.documentId) return;
  const data = message.data;
  if (data?.pending) { job.candidate = null; return; }
  if (data?.error) {
    job.candidate = null;
    if (!job.interactive || job.verifying) void finish(job,{error:['auth','permission','rate','offline'].includes(data.error)?data.error:'unsupported',
      retryAfterSeconds:Number.isFinite(data.retryAfterSeconds)?Math.max(1,Math.min(86400,Math.ceil(data.retryAfterSeconds))):900});
    else status = 'Complete Sign-In and Open the Credit Grants Page';
    return;
  }
  try {
    const reading = normalizeReading(data?.reading);
    if (!matches(reading,job)) { void finish(job,{error:'auth'}); return; }
    if (!job.interactive || job.verifying) void finish(job,{reading});
    else { job.candidate = reading; status = 'Ready to Track This Organization'; }
  } catch { void finish(job,{error:'unsupported'}); }
});
chrome.tabs.onRemoved.addListener(tabId => {
  const job = [...jobs.values()].find(j=>j.tabId === tabId);
  if (job) void finish(job,{error:'offline'});
});
chrome.alarms.onAlarm.addListener(() => connect());
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
async function initialize() {
  await chrome.alarms.create('reconnect',{periodInMinutes:0.5});
  // Recover only our temporary inactive tabs after an unexpected service-worker exit.
  const saved = await chrome.storage.session.get('ownedTabs');
  for (const tabId of saved.ownedTabs ?? []) {
    const tab = await chrome.tabs.get(tabId).catch(()=>null);
    if (tab && !tab.active) await chrome.tabs.remove(tabId).catch(()=>{});
  }
  await chrome.storage.session.set({ownedTabs:[]});
  connect();
}
// Service workers cannot use top-level await. Register listeners synchronously above.
export const initialized = initialize();
