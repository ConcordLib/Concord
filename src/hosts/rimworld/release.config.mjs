import { readFileSync } from 'node:fs';

import { declaredVersions, releaseConfig } from '@rimworks/mod-ci';

const WORKSHOP_ID = process.env.WORKSHOP_ID || '3758333473';

const config = releaseConfig({
  solution: 'Concord.RimWorld.slnx',
  versions: declaredVersions(readFileSync(new URL('./loadFolders.xml', import.meta.url), 'utf8')),
  mods: [{
    name: 'Concord',
    title: 'Concord: patching library',
    workshopId: WORKSHOP_ID,
  }],
  assets: [{ path: 'dist/Concord-*.zip', label: 'Concord mod' }],
  rootDir: process.env.SPLIT_PUBLISH === '1' ? '.' : 'src/hosts/rimworld',
});

/** @type {import('semantic-release').GlobalConfig} */
export default {
  ...config,
  tagFormat: 'rimworld-v${version}',
};
