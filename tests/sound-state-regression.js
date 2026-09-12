const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const source = fs.readFileSync(path.join(__dirname, '..', 'app', 'ZCodeWallpaper.cs'), 'utf8');
const match = source.match(/private const string InjectFn = @"([\s\S]*?)";\r?\n/);
assert(match, 'InjectFn not found');
const injectSource = match[1].replace(/""/g, '"');

function createPage(initialFocus) {
  let focused = initialFocus;
  const elements = new Map();
  const documentHandlers = {};
  const windowHandlers = {};

  class FakeStyle {
    constructor() { this.values = {}; }
    setProperty(name, value) { this.values[name] = String(value); }
    removeProperty(name) { delete this.values[name]; }
  }

  class FakeElement {
    constructor(tag) {
      this.tagName = tag.toUpperCase();
      this.children = [];
      this.style = new FakeStyle();
      this.paused = tag === 'video' || tag === 'audio';
      this.ended = false;
      this.muted = false;
      this.volume = 1;
      this.currentTime = 0;
      this.offsetWidth = 1;
      this.loadCount = 0;
    }
    set id(value) { this._id = value; if (value) elements.set(value, this); }
    get id() { return this._id; }
    setAttribute(name, value) { this[name] = value; }
    appendChild(child) { this.children.push(child); return child; }
    remove() { if (this._id) elements.delete(this._id); }
    play() { this.paused = false; return Promise.resolve(); }
    pause() { this.paused = true; }
    load() { this.loadCount += 1; this.paused = true; }
  }

  const documentElement = new FakeElement('html');
  const document = {
    body: {},
    documentElement,
    adoptedStyleSheets: [],
    visibilityState: 'visible',
    hasFocus: () => focused,
    createElement: tag => new FakeElement(tag),
    getElementById: id => elements.get(id) || null,
    addEventListener: (name, handler) => { documentHandlers[name] = handler; },
    removeEventListener: name => { delete documentHandlers[name]; }
  };
  const window = {
    addEventListener: (name, handler) => { windowHandlers[name] = handler; },
    removeEventListener: name => { delete windowHandlers[name]; }
  };
  class CSSStyleSheet { replaceSync() {} }

  const context = vm.createContext({
    window,
    document,
    CSSStyleSheet,
    JSON,
    Promise,
    setTimeout: () => 0,
    requestAnimationFrame: callback => callback()
  });
  const inject = vm.runInContext(`(${injectSource})`, context);

  return {
    inject,
    window,
    element: id => elements.get(id),
    setFocus(value) {
      focused = value;
      const handler = windowHandlers[value ? 'focus' : 'blur'];
      if (handler) handler();
    },
    setDocumentVisible(value) {
      document.visibilityState = value ? 'visible' : 'hidden';
      if (documentHandlers.visibilitychange) documentHandlers.visibilitychange();
    }
  };
}

function config(overrides) {
  return Object.assign({
    on: true,
    img: 'file:///wallpaper.mp4',
    video: true,
    css: '',
    opacity: 1,
    brightness: 1,
    saturation: 1,
    contrast: 1,
    overlay: 0,
    blur: 0,
    panelOpacity: 0.5,
    panelBlur: 10,
    theme: 'default',
    accentColor: '',
    glassColor: '',
    music: 'file:///music.mp3',
    soundSource: 'video',
    soundEnabled: true,
    soundVolume: 0.4,
    followWindow: true,
    zcodeVisible: true
  }, overrides || {});
}

function assertExclusive(page) {
  const video = page.element('zcode-wallpaper-video');
  const audio = page.element('zcode-wallpaper-audio');
  const audible = (video && !video.muted ? 1 : 0) + (audio && !audio.paused ? 1 : 0);
  assert(audible <= 1, 'video soundtrack and background music must never be active together');
}

(async () => {
  for (const sourceName of ['none', 'video', 'music']) {
    for (const enabled of [false, true]) {
      for (const followWindow of [false, true]) {
        for (const focused of [false, true]) {
          const matrixPage = createPage(focused);
          await matrixPage.inject(config({ soundSource: sourceName, soundEnabled: enabled, followWindow }));
          const matrixVideo = matrixPage.element('zcode-wallpaper-video');
          const matrixAudio = matrixPage.element('zcode-wallpaper-audio');
          const effective = sourceName !== 'none' && enabled;
          assert.strictEqual(matrixVideo.paused, false, 'matrix: video image must always keep playing');
          assert.strictEqual(!matrixVideo.muted, sourceName === 'video' && effective,
            'matrix: video soundtrack must follow only the selected source and effective gate');
          assert.strictEqual(!matrixAudio.paused, sourceName === 'music' && effective,
            'matrix: music must follow only the selected source and effective gate');
          assert.strictEqual(matrixPage.window.__ZCWP.getSoundState().effectiveEnabled, effective,
            'matrix: reported effective state must match the gate');
          assertExclusive(matrixPage);
        }
      }
    }
  }

  const page = createPage(true);
  await page.inject(config());
  assert.strictEqual(typeof page.window.__ZCWP.setSound, 'function', 'page protocol must expose setSound');
  assert.strictEqual(typeof page.window.__ZCWP.getSoundState, 'function', 'page protocol must expose getSoundState');

  const video = page.element('zcode-wallpaper-video');
  const audio = page.element('zcode-wallpaper-audio');
  assert.strictEqual(video.paused, false, 'video image must keep playing');
  assert.strictEqual(video.muted, false, 'focused video soundtrack must be audible');
  assert.strictEqual(audio.paused, true, 'inactive music source must be paused');
  assertExclusive(page);

  page.setFocus(false);
  assert.strictEqual(video.paused, false, 'focus loss must not pause the video image');
  assert.strictEqual(video.muted, false, 'focus loss alone must not mute visible ZCode');
  assert.strictEqual(page.window.__ZCWP.getSoundState().reason, 'playing');

  await page.window.__ZCWP.setSound({ zcodeVisible: false });
  assert.strictEqual(video.paused, false, 'screen occlusion must not pause the video image');
  assert.strictEqual(video.muted, true, 'screen occlusion must mute the video soundtrack');
  assert.strictEqual(page.window.__ZCWP.getSoundState().reason, 'notVisible');

  page.setFocus(true);
  assert.strictEqual(video.muted, true, 'focus alone must not override host visibility');
  await page.window.__ZCWP.setSound({ zcodeVisible: true });
  assert.strictEqual(video.muted, false, 'restored screen visibility must restore enabled video soundtrack');

  await page.window.__ZCWP.setSound({ source: 'music', enabled: true, volume: 0.25, followWindow: true });
  assert.strictEqual(video.muted, true, 'music selection must mute video soundtrack');
  assert.strictEqual(audio.paused, false, 'music selection must play background music');
  assert.strictEqual(audio.volume, 0.25, 'shared volume must apply to music');
  assertExclusive(page);

  const originalMusicLoads = audio.loadCount;
  await page.window.__ZCWP.setSound({ musicSrc: 'file:///music.mp3', volume: 0.3 });
  assert.strictEqual(audio.loadCount, originalMusicLoads, 'ordinary sound sync must not reload music');
  await page.window.__ZCWP.setSound({ musicSrc: 'file:///music.mp3', reloadMusic: true });
  assert.strictEqual(audio.loadCount, originalMusicLoads + 1,
    'explicit same-cache music replacement must reload the media element');
  assert.strictEqual(audio.paused, false, 'reloaded enabled music must resume through syncSound');

  await page.window.__ZCWP.setSound({ enabled: false });
  assert.strictEqual(audio.paused, true, 'manual music pause must persist as desired state');
  page.setFocus(false);
  page.setFocus(true);
  assert.strictEqual(audio.paused, true, 'focus return must not resurrect manually paused music');
  assert.strictEqual(page.window.__ZCWP.getSoundState().reason, 'disabled');

  await page.window.__ZCWP.setSound({ source: 'music', enabled: true, followWindow: true, zcodeVisible: true });
  page.setDocumentVisible(false);
  assert.strictEqual(audio.paused, true, 'hidden document must pause followed music');
  assert.strictEqual(page.window.__ZCWP.getSoundState().reason, 'notVisible');
  page.setDocumentVisible(true);
  assert.strictEqual(audio.paused, false, 'visible document must restore enabled followed music');

  await page.window.__ZCWP.setSound({ source: 'video', enabled: true, volume: 0.6, followWindow: false });
  page.setFocus(false);
  assert.strictEqual(video.paused, false, 'independent mode must keep video image playing');
  assert.strictEqual(video.muted, false, 'independent mode must ignore focus for video soundtrack');
  assert.strictEqual(video.volume, 0.6, 'shared volume must apply to video soundtrack');
  assertExclusive(page);

  const independentMusic = createPage(false);
  await independentMusic.inject(config({ soundSource: 'music', followWindow: false }));
  assert.strictEqual(independentMusic.element('zcode-wallpaper-audio').paused, false,
    'independent music must start during initial injection without focus');
  assert.strictEqual(independentMusic.element('zcode-wallpaper-video').muted, true);
  assertExclusive(independentMusic);

  await independentMusic.window.__ZCWP.setSound({ zcodeVisible: false });
  independentMusic.setDocumentVisible(false);
  assert.strictEqual(independentMusic.element('zcode-wallpaper-audio').paused, false,
    'independent music must ignore both host and document visibility');

  await independentMusic.window.__ZCWP.setSound({ source: 'none', enabled: false });
  assert.strictEqual(independentMusic.element('zcode-wallpaper-audio').paused, true);
  assert.strictEqual(independentMusic.element('zcode-wallpaper-video').muted, true);

  console.log('sound-state regression: PASS');
})().catch(error => {
  console.error(error.stack || error);
  process.exitCode = 1;
});
