// What can be downloaded, and how it is installed.
//
// Release assets carry no version in their names, precisely so these links can be permanent:
// releases/latest/download/<name> needs the exact file name, and the version lives in the tag.
// Shared by the documentation's download page and the brand download page, so the two can never
// drift apart.

export const RELEASE_BASE = 'https://github.com/fgilde/QuickRun/releases/latest/download';

export const PLATFORMS = [
  {
    os: 'windows',
    name: 'Windows',
    logo: 'windows',
    tint: '#0078d4',
    requirement: 'Windows 10 or later',
    command: 'winget install fgilde.QuickRun',
    alternative: 'scoop install https://quickrun.org/quickrun.json',
    builds: [
      { arch: 'x64', asset: 'quickrun-win-x64.zip' },
      { arch: 'arm64', asset: 'quickrun-win-arm64.zip' },
    ],
  },
  {
    os: 'macos',
    name: 'macOS',
    logo: 'apple',
    // Simple Icons ships this monochrome, so it needs inverting on a dark background.
    mono: true,
    tint: '#a2aaad',
    requirement: 'macOS 12 or later',
    // The cask, not the formula: it installs QuickRun.app into /Applications, so it appears in
    // Launchpad with its icon and can claim quickrun://, and it links the binary inside onto the
    // PATH so the terminal command is the same install.
    command: 'brew install --cask fgilde/tap/quickrun',
    alternative: 'brew install fgilde/tap/quickrun   # command line only',
    builds: [
      { arch: 'arm64', asset: 'quickrun-osx-arm64.tar.gz', label: 'Apple silicon' },
      { arch: 'x64', asset: 'quickrun-osx-x64.tar.gz', label: 'Intel' },
      { arch: 'app', asset: 'QuickRun-osx-arm64.app.zip', label: 'App bundle (Apple silicon)' },
    ],
  },
  {
    os: 'linux',
    name: 'Linux',
    logo: 'linux',
    mono: true,
    tint: '#f0b400',
    requirement: 'glibc 2.31 or later',
    command: 'curl -fsSL https://quickrun.org/install.sh | sh',
    builds: [
      { arch: 'x64', asset: 'quickrun-linux-x64.tar.gz' },
      { arch: 'arm64', asset: 'quickrun-linux-arm64.tar.gz' },
    ],
  },
];

/** What the visitor is most likely running, or null when it cannot be told. */
export function detectOs() {
  if (typeof navigator === 'undefined') return null;

  const agent = navigator.userAgent;
  const platform = navigator.userAgentData?.platform ?? navigator.platform ?? '';

  if (/Win/i.test(platform) || /Windows/i.test(agent)) return 'windows';
  if (/Mac/i.test(platform) || /Mac OS X/i.test(agent)) return 'macos';
  if (/Linux|X11/i.test(platform) || /Linux/i.test(agent)) return 'linux';
  return null;
}
