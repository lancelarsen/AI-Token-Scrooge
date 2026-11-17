export const PAGE = 'https://platform.openai.com/settings/organization/billing/credit-grants';
const id = x => typeof x === 'string' && /^[A-Za-z0-9_-]{1,200}$/.test(x);
const amount = x => typeof x === 'number' && Number.isFinite(x) && Math.abs(x) < 1e15;
export function normalizeReading(r) {
  if (!r || !id(r.subject) || !id(r.organization) || ![r.granted,r.used,r.available].every(amount) ||
      r.granted < 0 || r.used < 0 || Math.abs(r.granted-r.used-r.available) > 0.000001 ||
      !Array.isArray(r.grants) || r.grants.length > 1000) throw new Error('Invalid billing reading');
  const ids = new Set();
  const grants = r.grants.map(g => {
    if (!id(g.id) || ids.has(g.id) || !amount(g.remaining) || g.remaining <= 0 ||
        !(g.expiresAt === null || typeof g.expiresAt === 'string' && Number.isFinite(Date.parse(g.expiresAt))))
      throw new Error('Invalid grant');
    ids.add(g.id);
    return {id:g.id,remaining:g.remaining,expiresAt:g.expiresAt};
  });
  // Construct an allowlisted object, dropping any page-supplied extra properties.
  return {subject:r.subject,organization:r.organization,granted:r.granted,used:r.used,available:r.available,grants};
}
export function matches(r, job) {
  return (!job.subject || r.subject === job.subject) && (!job.organization || r.organization === job.organization);
}
