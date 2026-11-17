const status = document.querySelector('#status'), accounts = document.querySelector('#accounts');
let signature = '';
async function refresh() {
  const data = await chrome.runtime.sendMessage({type:'status'});
  status.textContent = data?.status ?? 'Open Scrooge to Connect';
  const next = JSON.stringify(data?.jobs);
  if (next === signature) return; signature = next;
  accounts.replaceChildren();
  for (const job of data?.jobs ?? []) {
    const card = document.createElement('section');
    if (job.candidate) {
      const r = job.candidate;
      for (const [text,cls] of [[new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(r.available),'balance'],
        [`Organization: ${r.organization}`,'note'],[`Account: ${r.subject}`,'note']]) {
        const p = document.createElement('p'); p.textContent = text; p.className = cls; card.append(p);
      }
      const button = document.createElement('button'); button.textContent = 'Track This Organization';
      button.onclick = async () => { button.disabled = true; await chrome.runtime.sendMessage({type:'accept',id:job.id}); await refresh(); };
      card.append(button);
    } else { const p=document.createElement('p'); p.textContent=job.verifying?'Verifying the Selected Organization':'Waiting for Billing Data'; card.append(p); }
    accounts.append(card);
  }
}
await refresh(); setInterval(()=>void refresh(),1000);
