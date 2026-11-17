// Runs in the Platform page, never in Google/OpenAI authentication pages.
// Observe the page's own two billing responses; never send requests or export credentials.
(() => {
  const LOGIN = 'https://api.openai.com/v1/dashboard/onboarding/login';
  const GRANTS = 'https://api.openai.com/v1/dashboard/billing/credit_grants';
  let identity = null, reading = null, latest = null, sequence = 0;
  const validId = x => typeof x === 'string' && /^[A-Za-z0-9_-]{1,200}$/.test(x);
  const number = x => typeof x === 'number' && Number.isFinite(x);
  function emit(value) {
    latest = value;
    window.postMessage({channel:'scrooge-billing-v1', ...value}, location.origin);
  }
  window.addEventListener('message', e => {
    if (e.source === window && e.origin === location.origin && e.data?.channel === 'scrooge-read-v1' && latest)
      window.postMessage({channel:'scrooge-billing-v1', ...latest}, location.origin);
  });
  function publish() {
    if (!identity || !reading) return;
    if (!identity.organizations.includes(reading.organization)) return emit({error:'auth'});
    emit({reading:{subject:identity.subject, ...reading}});
  }
  function parse(url, scope, data, started) {
    if (url === LOGIN) {
      const user = data?.user, orgs = user?.orgs?.data;
      if (!validId(user?.id) || !Array.isArray(orgs) || orgs.length > 1000 || orgs.some(o => !validId(o.id)))
        throw new Error('shape');
      identity = {subject:user.id, organizations:orgs.map(o => o.id)};
    } else {
      if (started !== sequence) return;
      const {total_granted:granted,total_used:used,total_available:available} = data;
      if (!validId(scope) || ![granted,used,available].every(number) || granted < 0 || used < 0 ||
          Math.abs(granted-used-available) > 0.000001 || !Array.isArray(data.grants?.data) || data.grants.data.length > 1000)
        throw new Error('shape');
      const ids = new Set(), grants = [], now = Date.now()/1000;
      for (const g of data.grants.data) {
        if (!validId(g.id) || ids.has(g.id) || ![g.grant_amount,g.used_amount].every(number) ||
            g.grant_amount < 0 || g.used_amount < 0 || typeof g.is_scheduled !== 'boolean' ||
            typeof g.is_held_for_prepaid_transition !== 'boolean' ||
            !(g.effective_at === null || number(g.effective_at)) || !(g.expires_at === null || number(g.expires_at)))
          throw new Error('shape');
        ids.add(g.id);
        if (!g.is_scheduled && !g.is_held_for_prepaid_transition && g.effective_at !== null && g.effective_at <= now &&
            (g.expires_at === null || g.expires_at > now) && g.grant_amount > g.used_amount)
          grants.push({id:g.id, remaining:g.grant_amount-g.used_amount,
            expiresAt:g.expires_at === null ? null : new Date(g.expires_at*1000).toISOString()});
      }
      reading = {organization:scope, granted,used,available,grants};
    }
    publish();
  }
  function failed(status, retry) {
    const seconds = /^\d+$/.test(retry ?? '') ? Number(retry) : Math.ceil((Date.parse(retry)-Date.now())/1000);
    emit({error:status === 401 ? 'auth' : status === 403 ? 'permission' : status === 429 ? 'rate' : 'offline',
      retryAfterSeconds:Number.isFinite(seconds) ? Math.max(1,Math.min(86400,seconds)) : 900});
  }
  const originalFetch = window.fetch;
  window.fetch = function(input, init) {
    let url, scope, started;
    try {
      url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, location.href).href;
      const method = (init?.method ?? input?.method ?? 'GET').toUpperCase();
      if (!(url === LOGIN && method === 'POST' || url === GRANTS && method === 'GET')) url = null;
      if (url === LOGIN) { identity = null; emit({pending:true}); }
      if (url === GRANTS) {
        scope = new Headers(init?.headers ?? input?.headers).get('OpenAI-Organization');
        started = ++sequence; reading = null; latest = null;
        emit({pending:true});
      }
    } catch { url = null; }
    const promise = originalFetch.apply(this, arguments);
    if (url) promise.then(async response => {
      if (url === GRANTS && started !== sequence) return;
      if (response.status !== 200) return failed(response.status,response.headers.get('Retry-After'));
      const reader = response.clone().body.getReader();
      try {
        let length = 0, chunks = [];
        for (;;) {
          const {done,value} = await reader.read(); if (done) break;
          length += value.length;
          if (length > 2000000) { await reader.cancel(); throw new Error('size'); }
          chunks.push(value);
        }
        const bytes = new Uint8Array(length); let offset = 0;
        for (const chunk of chunks) { bytes.set(chunk,offset); offset += chunk.length; }
        parse(url,scope,JSON.parse(new TextDecoder().decode(bytes)),started);
      } finally { reader.releaseLock(); }
    }).catch(() => emit({error:'unsupported'}));
    return promise;
  };
  // Some dashboard versions use XHR. Observe the same exact endpoints only.
  const requests = new WeakMap(), open = XMLHttpRequest.prototype.open,
    setHeader = XMLHttpRequest.prototype.setRequestHeader, send = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function(method,url) {
    requests.delete(this);
    try {
      const absolute = new URL(url,location.href).href;
      if (absolute === LOGIN && method.toUpperCase() === 'POST' || absolute === GRANTS && method.toUpperCase() === 'GET')
        requests.set(this,{url:absolute,scope:null});
    } catch { /* Leave unrelated requests alone. */ }
    return open.apply(this,arguments);
  };
  XMLHttpRequest.prototype.setRequestHeader = function(name,value) {
    const r = requests.get(this);
    if (r && name.toLowerCase() === 'openai-organization') r.scope = value;
    return setHeader.apply(this,arguments);
  };
  XMLHttpRequest.prototype.send = function() {
    const r = requests.get(this);
    if (r) {
      if (r.url === LOGIN) { identity = null; emit({pending:true}); }
      if (r.url === GRANTS) { r.started = ++sequence; reading = null; emit({pending:true}); }
      this.addEventListener('load', () => {
        if (r.url === GRANTS && r.started !== sequence) return;
        try {
          if (this.status !== 200) return failed(this.status,this.getResponseHeader('Retry-After'));
          const text = this.responseType === 'json' ? JSON.stringify(this.response) : this.responseText;
          if (text.length > 2000000) throw new Error('size');
          parse(r.url,r.scope,JSON.parse(text),r.started);
        } catch { emit({error:'unsupported'}); }
      }, {once:true});
    }
    return send.apply(this,arguments);
  };
})();
