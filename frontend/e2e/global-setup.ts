import { execSync } from 'child_process';
import * as path from 'path';

export default async function globalSetup(): Promise<void> {
  const rootDir = path.resolve(__dirname, '../../');
  const seedProject = path.resolve(rootDir, 'tools/seed');

  try {
    const output = execSync(
      `dotnet run --project "${seedProject}" -- --tenant-name "Apex Mobiles Demo" --subdomain apex`,
      {
        encoding: 'utf-8',
        env: { ...process.env }
      }
    );

    const match = output.match(/Tenant ID:\s*([a-f0-9\-]+)/i);
    if (match) {
      const tenantId = match[1].trim();
      process.env['SEED_TENANT_ID'] = tenantId;
      const fs = require('fs');
      fs.writeFileSync(path.join(__dirname, '.seed-tenant-id'), tenantId, 'utf-8');
      console.log(`[Global Setup] Seeded Tenant ID: ${tenantId}`);
    }
  } catch (err) {
    console.warn('[Global Setup] Seed execution warning (stack may be managed externally):', err);
  }
}
