// Isolated world: only relay from the top-level Platform document, and only for a requested tab.
(() => {
  if (window !== window.top) return;
  let armed = false;
  window.addEventListener('message', e => {
    if (!armed || e.source !== window || e.origin !== 'https://platform.openai.com' ||
        e.data?.channel !== 'scrooge-billing-v1') return;
    const data = e.data;
    if (JSON.stringify(data).length > 131000) return;
    chrome.runtime.sendMessage({type:'capture',data}).catch(() => {});
  });
  chrome.runtime.sendMessage({type:'ready'}).then(reply => {
    armed = reply?.armed === true;
    if (armed) window.postMessage({channel:'scrooge-read-v1'},location.origin);
  }).catch(() => {});
})();
