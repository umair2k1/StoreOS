#!/usr/bin/env python3
"""Consistency checker for the StoreOS prompt pack. Run from anywhere: python3 tools/verify_prompts.py
Exit code 0 = OK, 1 = problems found. Only uses the standard library."""
import re, sys, glob, os, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
P = os.path.join(ROOT, 'prompts')
problems = []
def bad(msg): problems.append(msg)

files = {re.match(r'(P-\d+)-', os.path.basename(f)).group(1): f for f in glob.glob(os.path.join(P, 'P-*.md'))}
idx = open(os.path.join(P, 'INDEX.md'), encoding='utf-8').read()
order = re.findall(r'^\| \d+ \| (P-\d+) \| `([^`]+)` \|', idx, re.M)
ids = [i for i, _ in order]

# 1. INDEX <-> files
if len(set(ids)) != len(ids): bad('INDEX has duplicate prompt ids')
for pid, fn in order:
    if not os.path.exists(os.path.join(P, fn)): bad(f'INDEX lists missing file {fn}')
for pid in files:
    if pid not in ids: bad(f'{pid} has a file but is not in INDEX')
nums = [int(re.search(r'\| (\d+) \| P-', l).group(1)) for l in idx.splitlines() if re.match(r'^\| \d+ \| P-', l)]
if nums != list(range(1, len(nums) + 1)): bad('INDEX sequence numbers are not 1..N')

# 2. anatomy + header
HEADS = ['1. ROLE & MISSION', '2. WORLD STATE', '3. CONTEXT BLOCK', '4. TASK', '5. HARD CONSTRAINTS',
         '6. FORBIDDEN PATTERNS', '7. VERIFICATION', '8. FAILURE PROTOCOL', '9. REPORT']
hdr = {}
for pid, f in files.items():
    t = open(f, encoding='utf-8').read()
    if re.findall(r'^## (\d\. [A-Z &]+)$', t, re.M) != HEADS: bad(f'{pid}: section headings are not the 9-section anatomy')
    m = re.search(r'\*\*Phase:\*\* (\S+) &nbsp;\|&nbsp; \*\*Minimum executor reasoning level:\*\* (\w+)', t)
    if not m or m.group(2) not in ('low', 'med', 'high'): bad(f'{pid}: bad header line'); continue
    hdr[pid] = m.groups()
    if not t.startswith(f'# {pid} '): bad(f'{pid}: title line must start with "# {pid} "')
    if 'Global invariants (apply to all backend code in this repo)' in t and 'Layering: StoreOS.Application NEVER' not in t:
        bad(f'{pid}: carries the OLD global-invariants block (no layering/transaction rules)')
    if f'BLOCKED: {pid}' not in t: bad(f'{pid}: failure protocol does not name its own id')

# 3. MANIFEST
man = open(os.path.join(P, 'MANIFEST.md'), encoding='utf-8').read()
rows = {m.group(1): (m.group(2), m.group(6)) for m in re.finditer(r'^\| (P-\d+) \| (\d+) \| (.*?) \| (.*?) \| (.*?) \| (\w+) \| (.*?) \|$', man, re.M)}
for pid in ids:
    if pid not in rows: bad(f'{pid}: missing from MANIFEST'); continue
    if pid in hdr and (hdr[pid][0], hdr[pid][1]) != rows[pid]: bad(f'{pid}: MANIFEST phase/level {rows[pid]} != header {hdr[pid]}')
for pid in rows:
    if pid not in ids: bad(f'{pid}: in MANIFEST but not in INDEX')

# 4. references
for pid, f in files.items():
    t = open(f, encoding='utf-8').read()
    for ref in set(re.findall(r'\bP-(\d{3,4})\b', t)):
        if 'P-' + ref not in ids and 'P-' + ref != 'P-000': bad(f'{pid}: refers to unknown prompt P-{ref}')
    for fn in set(re.findall(r'`(P-\d+-[a-z0-9-]+\.md)`', t)):
        if not os.path.exists(os.path.join(P, fn)): bad(f'{pid}: refers to missing file {fn}')

# 5. stale designs / layering inside C# and SQL code blocks
STALE = [r'TenantTransactionMiddleware', r'IdempotencyMiddleware', r'PhoneZone\.', r'StoreOS\.Application\.Exceptions',
         r'SET LOCAL transaction_isolation', r'\.EnableRetryOnFailure\(']
for pid, f in files.items():
    t = open(f, encoding='utf-8').read()
    for b in re.findall(r'```(?:csharp|sql)\n(.*?)```', t, re.S):
        code = re.sub(r'//.*', '', b)
        for pat in STALE:
            if re.search(pat, code): bad(f'{pid}: stale pattern /{pat}/ inside a code block')
        if re.search(r'namespace StoreOS\.Application', b):
            if re.search(r'using StoreOS\.Infrastructure', b): bad(f'{pid}: Application-layer code block uses StoreOS.Infrastructure')
            if re.search(r'\bStoreOsDbContext\b|\bReportingDbContext\b', code): bad(f'{pid}: Application-layer code block uses a concrete DbContext (use IAppDbContext)')
        if re.search(r"current_setting\('app\.(tenant_id|branch_ids|all_branches)', true\)", code) and 'NULLIF' not in code and 'CREATE POLICY' in code:
            bad(f'{pid}: RLS policy without NULLIF(current_setting(...), \'\')')
        if 'CREATE POLICY' in code and re.search(r'IS NULL', code) and 'current_setting' in code: bad(f'{pid}: RLS policy contains an IS NULL bypass')

# 6. shared files
for s in ('_shared/CONTRACTS.md', '_shared/DECISIONS.md'):
    if not os.path.exists(os.path.join(P, s)): bad(f'missing {s}')

if problems:
    print('PROBLEMS:'); [print(' -', p) for p in problems]; sys.exit(1)
print(f'OK: {len(ids)} prompts, INDEX/MANIFEST/headers consistent, no stale patterns in code blocks.')
