// Edits the agents' settings.json (hooks.json for Codex) for hooks/install.sh and uninstall.sh on
// macOS, with the JavaScript that ships with it (osascript -l JavaScript): no jq, no Node.js.
// Same result as the jq programs of settings-json.sh, printed with two-space indents.
//
//   osascript -l JavaScript settings-json.js check  <file>             fails unless a JSON object
//   osascript -l JavaScript settings-json.js format <file>             the file, re-indented
//   osascript -l JavaScript settings-json.js merge  <file> <entries>   our hooks added, earlier copies dropped
//   osascript -l JavaScript settings-json.js strip  <file>             our hooks removed
//
// A missing or empty file reads as {}. <entries> is {"Event": [matcher groups], ...}.

ObjC.import('Foundation');

function read(path) {
  const fm = $.NSFileManager.defaultManager;
  if (!path || !fm.fileExistsAtPath(path)) return {};
  const text = $.NSString.stringWithContentsOfFileEncodingError(path, $.NSUTF8StringEncoding, null);
  if (text.isNil()) throw new Error(path + ': cannot read it as UTF-8');
  const s = ObjC.unwrap(text);
  if (s.trim() === '') return {};
  const value = JSON.parse(s);
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error(path + ': not a JSON object');
  return value;
}

const ours = (hook) => String((hook && hook.command) || '').indexOf('agent-haptics') !== -1;

// Matcher groups without any agent-haptics command; groups left empty are dropped.
function withoutOurs(groups) {
  if (!Array.isArray(groups)) return groups;
  return groups
    .map((g) => (g && Array.isArray(g.hooks) ? Object.assign({}, g, { hooks: g.hooks.filter((h) => !ours(h)) }) : g))
    .filter((g) => !(g && Array.isArray(g.hooks) && g.hooks.length === 0));
}

function merge(settings, entries) {
  if (settings.hooks === undefined || settings.hooks === null) settings.hooks = {};
  if (typeof settings.hooks !== 'object' || Array.isArray(settings.hooks)) throw new Error('"hooks" is not an object');
  for (const event of Object.keys(entries)) {
    const kept = withoutOurs(settings.hooks[event] || []);
    settings.hooks[event] = (Array.isArray(kept) ? kept : []).concat(entries[event]);
  }
  return settings;
}

function strip(settings) {
  const hooks = settings.hooks;
  if (!hooks || typeof hooks !== 'object' || Array.isArray(hooks)) return settings;
  for (const event of Object.keys(hooks)) {
    hooks[event] = withoutOurs(hooks[event]);
    if (Array.isArray(hooks[event]) && hooks[event].length === 0) delete hooks[event];
  }
  if (Object.keys(hooks).length === 0) delete settings.hooks;
  return settings;
}

function run(argv) {
  const [command, path, entries] = argv;
  const settings = read(path);
  switch (command) {
    case 'check': return '';
    case 'format': return JSON.stringify(settings, null, 2);
    case 'merge': return JSON.stringify(merge(settings, JSON.parse(entries)), null, 2);
    case 'strip': return JSON.stringify(strip(settings), null, 2);
    default: throw new Error('usage: settings-json.js check|format|merge|strip <file> [entries]');
  }
}
