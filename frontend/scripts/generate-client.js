const { execSync } = require('child_process');
const path = require('path');

const env = { ...process.env, DOTNET_ROLL_FORWARD: 'Major' };
const projectPath = path.resolve(__dirname, '../../backend/src/StoreOS.Api/StoreOS.Api.csproj');
const openApiPath = path.resolve(__dirname, '../src/app/core/api/openapi.json');
const nswagConfig = path.resolve(__dirname, '../nswag.json');

console.log('Generating OpenAPI specification from StoreOS.Api...');
execSync(`npx nswag aspnetcore2openapi /project:"${projectPath}" /noBuild:true /output:"${openApiPath}"`, {
  stdio: 'inherit',
  env,
  cwd: path.resolve(__dirname, '..')
});

console.log('Generating TypeScript client from OpenAPI specification...');
execSync(`npx nswag run "${nswagConfig}"`, {
  stdio: 'inherit',
  env,
  cwd: path.resolve(__dirname, '..')
});

console.log('API client generation complete.');
