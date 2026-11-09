const versionArgs = [
  '-p:MinVerVersionOverride=${nextRelease.version}',
  '-p:Version=${nextRelease.version}',
  '-p:PackageVersion=${nextRelease.version}',
  "-p:FileVersion=${nextRelease.version.replace(/-.*/, '')}.0",
  "-p:AssemblyVersion=${nextRelease.version.replace(/-.*/, '')}.0",
  '-p:InformationalVersion=${nextRelease.version}',
].join(' ');

const prepareCmd = [
  `dotnet build Concord.BepInEx.slnx -c Release -p:ElinDir=$ELIN_DIR ${versionArgs}`,
  'zip_out="$PWD/Concord.BepInEx.zip"',
  'rm -rf "$RUNNER_TEMP/stage" "$zip_out"',
  'mkdir -p "$RUNNER_TEMP/stage/Concord"',
  'cp plugin/* "$RUNNER_TEMP/stage/Concord/"',
  '(cd "$RUNNER_TEMP/stage" && zip -qr "$zip_out" Concord)',
].join(' && ');

/** @type {import('semantic-release').GlobalConfig} */
export default {
  branches: ['main'],
  tagFormat: 'bepinex-v${version}',
  plugins: [
    ['@semantic-release/commit-analyzer', { preset: 'conventionalcommits' }],
    ['@semantic-release/release-notes-generator', { preset: 'conventionalcommits' }],
    ['@semantic-release/exec', { prepareCmd }],
    ['@semantic-release/github', {
      assets: [{
        path: 'Concord.BepInEx.zip',
        name: 'Concord.BepInEx-${nextRelease.version}.zip',
        label: 'Concord for BepInEx ${nextRelease.version} (unzip into BepInEx/plugins)',
      }],
    }],
  ],
};
