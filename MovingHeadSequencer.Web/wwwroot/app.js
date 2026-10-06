const elements = Object.fromEntries(
  [...document.querySelectorAll('[id]')].map(element => [element.id, element])
);

const state = {
  document: null,
  selectedIndex: 0,
  currentMs: 0,
  playing: false,
  lastFrame: 0,
  compileTimer: null,
  compileRevision: 0,
  toastTimer: null,
  timingSourceName: null,
  loopSelected: false,
  audioUrl: null,
  audioFileName: null,
  waveformPeaks: null,
  mountMode: 'floor',
  fixtureRotations: [],
  previewHeads: [],
  suggestionRevision: 0,
  suggestions: null,
  regenerationUndo: null,
  regenerationSummary: null,
  appliedRegenerationChoices: null,
  activeTimelineIndex: -1,
  timelineGeometry: [],
  timelineContentWidth: 0,
  backupCatalog: null,
  projectSaveTimer: null,
  projectSaveRevision: 0,
  projectRevision: null,
  workspaceRoot: null,
};

const dynamicPan = new Set(['OpenFan', 'CloseFan', 'CloseOppositeFan', 'Cross', 'Uncross', 'Bounce']);
const dynamicTilt = new Set(['Rise', 'Fall', 'Bounce', 'InverseBounce']);
const rhythmicPan = new Set(['PanBounceEveryTwoBeats', 'PanTiltBounceEveryFourBeats']);
const rhythmicTilt = new Set([
  'TiltLiftEveryBeat',
  'TiltPulseEveryHalfBeat',
  'TiltBounceEveryBeat',
  'TiltBounceEveryTwoBeats',
  'TiltSyncopatedPulse',
  'TiltOddEvenEveryBeat',
  'TiltCenterOuterEveryBeat',
  'TiltHeadChaseEveryBeat',
  'TiltPairChaseEveryBeat',
  'PanTiltBounceEveryFourBeats',
]);
const generatedPresetValue = '__generated__';
const projectFormat = 'moving-head-studio-project';
const projectVersion = 1;

bindEvents();
loadBootstrap('', 4);

function bindEvents() {
  elements.sequenceSelect.addEventListener('change', () => {
    loadBootstrap(elements.sequenceSelect.value, state.document?.headCount ?? 4);
  });

  elements.headCountInput.addEventListener('change', () => {
    const headCount = Number(elements.headCountInput.value);
    if (!Number.isInteger(headCount) || headCount < 2 || headCount > 12) {
      elements.headCountInput.value = state.document?.headCount ?? 4;
      showToast('Head count must be a whole number from 2 through 12.', true);
      return;
    }
    if (headCount !== state.document?.headCount) {
      loadBootstrap(state.document?.sequenceId ?? '', headCount);
    }
  });
  document.querySelectorAll('[data-mount-mode]').forEach(button => {
    button.addEventListener('click', () => setMountMode(button.dataset.mountMode));
  });
  elements.previewSetupButton.addEventListener('click', () => {
    setPreviewSetupOpen(elements.previewSetupPopover.hidden);
  });
  elements.previewSetupCloseButton.addEventListener('click', () => setPreviewSetupOpen(false));
  elements.previewAllHeadsButton.addEventListener('click', () => setPreviewHeads([]));
  elements.previewOddHeadsButton.addEventListener('click', () => setPreviewHeads(
    range(1, state.document.headCount).filter(number => number % 2 === 1)
  ));
  elements.previewEvenHeadsButton.addEventListener('click', () => setPreviewHeads(
    range(1, state.document.headCount).filter(number => number % 2 === 0)
  ));
  document.addEventListener('pointerdown', event => {
    if (!elements.previewSetupPopover.hidden && !elements.previewSetupWrap.contains(event.target)) {
      setPreviewSetupOpen(false);
    }
    if (elements.projectMenu.open && !elements.projectMenu.contains(event.target)) {
      elements.projectMenu.open = false;
    }
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !elements.previewSetupPopover.hidden) {
      setPreviewSetupOpen(false);
      elements.previewSetupButton.focus();
    }
    if (event.key === 'Escape' && !elements.validationDrawer.hidden) {
      setValidationDrawerOpen(false);
      elements.validationStatusButton.focus();
    }
  });

  elements.playButton.addEventListener('click', togglePlayback);
  elements.loopButton.addEventListener('click', () => {
    setLoopSelected(!state.loopSelected);
  });
  elements.restartButton.addEventListener('click', () => {
    state.playing = false;
    state.currentMs = 0;
    elements.audioElement.pause();
    if (state.audioUrl) elements.audioElement.currentTime = 0;
    syncPlaybackUi();
  });
  elements.playhead.addEventListener('input', () => {
    state.playing = false;
    state.currentMs = Number(elements.playhead.value);
    elements.audioElement.pause();
    if (state.audioUrl) elements.audioElement.currentTime = state.currentMs / 1000;
    syncPlaybackUi();
  });
  elements.timingSourceSelect.addEventListener('change', () => {
    state.timingSourceName = elements.timingSourceSelect.value;
    renderTimingSummary();
    drawTimingGrid();
    scheduleCompile();
  });
  elements.timeline.addEventListener('pointerdown', event => {
    if (event.target.closest('.timeline-cue')) return;
    const bounds = elements.timeline.getBoundingClientRect();
    state.playing = false;
    const x = event.clientX - bounds.left + elements.timeline.scrollLeft;
    state.currentMs = timelineTimeForX(x);
    syncPlaybackUi();
  });

  elements.cueName.addEventListener('input', () => mutateSection(section => {
    section.name = elements.cueName.value || 'Cue';
  }, { compile: false }));
  elements.cueName.addEventListener('change', scheduleCompile);
  elements.cueStart.addEventListener('change', updateStartBoundary);
  elements.cueEnd.addEventListener('change', updateEndBoundary);
  elements.presetSelect.addEventListener('change', applyPreset);
  elements.panSelect.addEventListener('change', () => setCustomProperty('pan', elements.panSelect.value));
  elements.tiltSelect.addEventListener('change', () => setCustomProperty('tilt', elements.tiltSelect.value));
  elements.rhythmSelect.addEventListener('change', setRhythmPattern);
  elements.motionEnergySelect.addEventListener('change', () =>
    setCustomProperty('motionEnergy', elements.motionEnergySelect.value));
  elements.motionShapeSelect.addEventListener('change', () =>
    setCustomProperty('motionShape', elements.motionShapeSelect.value));
  elements.motionPhaseSelect.addEventListener('change', () =>
    setCustomProperty('motionPhase', elements.motionPhaseSelect.value));
  elements.dimmerSelect.addEventListener('change', () => setCustomProperty('dimmer', elements.dimmerSelect.value));
  elements.intensityEnvelopeSelect.addEventListener('change', () =>
    setCustomProperty('intensityEnvelope', elements.intensityEnvelopeSelect.value));
  elements.shutterToggle.addEventListener('change', () => setCustomProperty('shutterOpen', elements.shutterToggle.checked));
  elements.outputFileName.addEventListener('input', () => {
    elements.outputName.textContent = elements.outputFileName.value;
    scheduleProjectSave();
  });

  elements.allHeadsButton.addEventListener('click', () => setHeads([]));
  elements.oddHeadsButton.addEventListener('click', () => setHeads(
    range(1, state.document.headCount).filter(number => number % 2 === 1)
  ));
  elements.evenHeadsButton.addEventListener('click', () => setHeads(
    range(1, state.document.headCount).filter(number => number % 2 === 0)
  ));
  elements.rotationButtons.addEventListener('click', event => {
    const button = event.target.closest('[data-rotation]');
    if (button) setFixtureRotation(Number(button.dataset.rotation));
  });
  elements.refreshSuggestionsButton.addEventListener('click', loadSuggestions);
  elements.regenerateSongButton.addEventListener('click', regenerateWholeSong);
  elements.undoSuggestionButton.addEventListener('click', undoRegeneration);
  elements.suggestionChoices.addEventListener('click', event => {
    const button = event.target.closest('[data-suggestion-id]');
    if (button) applyRegenerationChoice(button.dataset.suggestionId);
  });
  elements.splitButton.addEventListener('click', splitSection);
  elements.deleteButton.addEventListener('click', deleteSection);

  elements.importProjectButton.addEventListener('click', () => {
    elements.projectMenu.open = false;
    elements.projectFileInput.click();
  });
  elements.projectFileInput.addEventListener('change', importProjectFile);
  elements.exportProjectButton.addEventListener('click', () => {
    elements.projectMenu.open = false;
    exportProjectCopy();
  });
  elements.resetProjectButton.addEventListener('click', () => {
    elements.projectMenu.open = false;
    resetHiddenProject();
  });
  elements.workspaceButton.addEventListener('click', openWorkspaceDialog);
  elements.emptyWorkspaceButton.addEventListener('click', openWorkspaceDialog);
  elements.workspaceMenuButton.addEventListener('click', () => {
    elements.projectMenu.open = false;
    openWorkspaceDialog();
  });
  elements.workspaceDialogCloseButton.addEventListener('click', closeWorkspaceDialog);
  elements.cancelWorkspaceButton.addEventListener('click', closeWorkspaceDialog);
  elements.confirmWorkspaceButton.addEventListener('click', () =>
    switchWorkspace(elements.workspacePathInput.value));
  elements.browseWorkspaceButton.addEventListener('click', pickWorkspace);
  elements.workspacePathInput.addEventListener('keydown', event => {
    if (event.key === 'Enter') switchWorkspace(elements.workspacePathInput.value);
  });
  elements.workspaceDialog.addEventListener('click', event => {
    if (event.target === elements.workspaceDialog) closeWorkspaceDialog();
  });
  elements.generateButton.addEventListener('click', openGenerateDialog);
  elements.generateDialogCloseButton.addEventListener('click', closeGenerateDialog);
  elements.cancelGenerateButton.addEventListener('click', closeGenerateDialog);
  elements.confirmGenerateButton.addEventListener('click', generateSequence);
  elements.generationModeControl.addEventListener('change', renderGenerationMode);
  elements.refreshBackupsButton.addEventListener('click', loadBackups);
  elements.backupList.addEventListener('click', event => {
    const button = event.target.closest('[data-backup-id]');
    if (button) restoreBackup(button.dataset.backupId);
  });
  elements.generateDialog.addEventListener('click', event => {
    if (event.target === elements.generateDialog) closeGenerateDialog();
  });
  elements.validationStatusButton.addEventListener('click', () => setValidationDrawerOpen(true));
  elements.validationDrawerCloseButton.addEventListener('click', () => setValidationDrawerOpen(false));
  elements.validationDrawerBackdrop.addEventListener('click', () => setValidationDrawerOpen(false));
  elements.panMinimumInput.addEventListener('change', () => updateMovementLimits('pan'));
  elements.panMaximumInput.addEventListener('change', () => updateMovementLimits('pan'));
  elements.tiltMinimumInput.addEventListener('change', () => updateMovementLimits('tilt'));
  elements.tiltMaximumInput.addEventListener('change', () => updateMovementLimits('tilt'));
  elements.panMinimumInput.addEventListener('input', () => updateMovementSlider('pan', 'minimum'));
  elements.panMaximumInput.addEventListener('input', () => updateMovementSlider('pan', 'maximum'));
  elements.tiltMinimumInput.addEventListener('input', () => updateMovementSlider('tilt', 'minimum'));
  elements.tiltMaximumInput.addEventListener('input', () => updateMovementSlider('tilt', 'maximum'));
  elements.panTravelDegreesInput.addEventListener('change', () => updateTravelDegrees('pan'));
  elements.tiltTravelDegreesInput.addEventListener('change', () => updateTravelDegrees('tilt'));
  elements.panTravelDegreesInput.addEventListener('input', () => renderMovementRangeVisual('pan'));
  elements.tiltTravelDegreesInput.addEventListener('input', () => renderMovementRangeVisual('tilt'));
  elements.mediaReadout.addEventListener('click', () => elements.audioFileInput.click());
  elements.audioFileInput.addEventListener('change', linkAudio);
  elements.volumeSlider.addEventListener('input', () => {
    elements.audioElement.volume = Number(elements.volumeSlider.value);
  });
  elements.audioElement.addEventListener('ended', async () => {
    const loop = state.loopSelected ? selectedSection() : null;
    if (loop && state.audioUrl) {
      state.currentMs = loop.startMs;
      elements.audioElement.currentTime = state.currentMs / 1000;
      try {
        await elements.audioElement.play();
        state.lastFrame = performance.now();
        syncPlaybackUi();
        return;
      } catch (error) {
        showToast(`Audio playback failed: ${error.message}`, true);
      }
    }
    state.playing = false;
    syncPlaybackUi();
  });
  window.addEventListener('resize', () => {
    drawStage();
    cacheTimelineGeometry();
    updateTimelinePlayhead();
    drawTimingGrid();
  });
}

async function loadBootstrap(sequenceId, headCount, { ignoreProject = false } = {}) {
  setBusy(true, 'Loading sequence');
  try {
    const previousSequence = state.document?.sequenceId;
    const previousHeadCount = state.document?.headCount;
    const baseDocument = await api(
      `/api/bootstrap?sequence=${encodeURIComponent(sequenceId)}&headCount=${headCount}`
    );
    let document = baseDocument;
    let project = null;
    let relinkProject = false;
    if (!ignoreProject) {
      const loaded = await api(
        `/api/project?sequence=${encodeURIComponent(baseDocument.sequenceId)}&headCount=${headCount}`
      );
      if (loaded.warning) showToast(loaded.warning, true);
      project = loaded.project;
      if (project && project.source.fingerprint !== baseDocument.sourceFingerprint) {
        const useSaved = window.confirm(
          `"${baseDocument.sourceFileName}" changed after its editor project was saved. ` +
          'Choose OK to apply the saved project to the current sequence, or Cancel to rebuild from the sequence.'
        );
        if (!useSaved) {
          await api('/api/project/reset', {
            method: 'POST',
            body: JSON.stringify({ sequenceId: baseDocument.sequenceId, headCount }),
          });
          project = null;
        } else {
          relinkProject = true;
        }
      }
      if (project) {
        try {
          document = await compileStoredProject(
            project,
            baseDocument.sequenceId,
            baseDocument.sourceFingerprint
          );
        } catch (error) {
          project = null;
          showToast(`Saved project could not be loaded: ${error.message}`, true);
        }
      }
    }
    if (previousSequence && previousSequence !== document.sequenceId) clearLinkedAudio();
    if (previousSequence !== document.sequenceId || previousHeadCount !== document.headCount) {
      elements.outputFileName.value = project?.editor.outputFileName || document.suggestedOutputFileName;
    }
    state.document = document;
    state.workspaceRoot = document.workspaceRoot;
    setEmptyWorkspace(false);
    state.projectRevision = project?.revision ?? null;
    state.fixtureRotations = project
      ? normalizeProjectRotations(project.preview.fixtureRotations, document.headCount)
      : loadFixtureRotations(document.sequenceId, document.headCount);
    state.mountMode = project?.preview.mountMode === 'truss' ? 'truss' : 'floor';
    applyMountMode();
    state.previewHeads = [];
    state.suggestions = null;
    state.regenerationUndo = null;
    state.regenerationSummary = project?.regeneration.summary ?? null;
    state.appliedRegenerationChoices = project?.regeneration.appliedChoices ?? null;
    state.backupCatalog = null;
    state.selectedIndex = Math.max(0, Math.min(
      project?.view.selectedCueIndex ?? 0,
      document.cueSheet.sections.length - 1
    ));
    state.currentMs = Math.max(0, Math.min(
      project?.view.currentMs ?? document.cueSheet.sections[state.selectedIndex].startMs,
      document.preview.durationMs
    ));
    state.playing = false;
    state.timingSourceName = document.timing.beatSources.some(source =>
      source.trackName === project?.editor.timingSourceName
    ) ? project.editor.timingSourceName : document.timing.defaultBeatSource;
    setLoopSelected(false);
    renderDocument({ rebuildSelectors: true });
    elements.workspaceStatus.textContent = project ? 'Saved project loaded' : 'Auto-saved locally';
    setStatus(project ? 'Project loaded' : 'Ready');
    if (!project || relinkProject) scheduleProjectSave(0);
    loadSuggestions();
  } catch (error) {
    if (/No valid XSQ sequences were found/i.test(error.message)) {
      await enterEmptyWorkspace();
    } else {
      showToast(error.message, true);
      setStatus('Load failed');
    }
  } finally {
    setBusy(false);
  }
}

async function compileStoredProject(project, sequenceId, sourceFingerprint) {
  return api('/api/preview', {
    method: 'POST',
    body: JSON.stringify({
      sequenceId,
      headCount: project.editor.headCount,
      cueSheet: project.editor.cueSheet,
      fixtureProfile: project.editor.fixtureProfile,
      timingSourceName: project.editor.timingSourceName,
      outputFileName: project.editor.outputFileName,
      force: false,
      expectedSourceFingerprint: sourceFingerprint,
    }),
  });
}

async function compileNow() {
  if (!state.document) return;
  clearTimeout(state.compileTimer);
  const revision = ++state.compileRevision;
  setStatus('Validating');
  try {
    const document = await api('/api/preview', {
      method: 'POST',
      body: JSON.stringify(createRequest()),
    });
    if (revision !== state.compileRevision) return;
    const outputFileName = elements.outputFileName.value;
    state.document = document;
    state.selectedIndex = Math.min(state.selectedIndex, document.cueSheet.sections.length - 1);
    elements.outputFileName.value = outputFileName || document.suggestedOutputFileName;
    renderDocument({ rebuildSelectors: false });
    setStatus('Ready');
    scheduleProjectSave();
    loadSuggestions();
  } catch (error) {
    if (revision !== state.compileRevision) return;
    if (/source sequence changed outside the editor/i.test(error.message)) {
      showToast(error.message, true);
      await loadBootstrap(state.document.sequenceId, state.document.headCount);
      return;
    }
    setStatus('Needs attention');
    showToast(error.message, true);
  }
}

function scheduleCompile() {
  clearTimeout(state.compileTimer);
  clearTimeout(state.projectSaveTimer);
  elements.workspaceStatus.textContent = 'Unsaved changes';
  setStatus('Edited');
  state.compileTimer = setTimeout(compileNow, 260);
}

function renderDocument({ rebuildSelectors }) {
  if (!state.document) return;
  if (rebuildSelectors) {
    populateSequenceSelector();
    populatePatternSelectors();
    populateTimingSources();
  }
  elements.headCountInput.value = state.document.headCount;
  elements.playhead.max = state.document.preview.durationMs;
  elements.playhead.value = Math.min(state.currentMs, state.document.preview.durationMs);
  elements.outputFileName.value ||= state.document.suggestedOutputFileName;
  elements.outputName.textContent = elements.outputFileName.value;
  renderWorkspaceRoot();
  renderInspector();
  renderPreviewFixtureButtons();
  renderRotationButtons();
  renderTimeline();
  renderValidation();
  renderRegenerationSummary();
  renderTimingSummary();
  syncPlaybackUi();
}

function renderWorkspaceRoot() {
  const workspaceRoot = state.document?.workspaceRoot ?? state.workspaceRoot;
  if (!workspaceRoot) return;
  state.workspaceRoot = workspaceRoot;
  const path = workspaceRoot.replace(/[\\/]+$/, '');
  const folderName = path.split(/[\\/]/).at(-1) || workspaceRoot;
  elements.workspaceName.textContent = folderName;
  elements.workspaceButton.title = `Change workspace · ${workspaceRoot}`;
  elements.workspaceButton.setAttribute(
    'aria-label',
    `Change workspace. Current folder: ${workspaceRoot}`
  );
  elements.workspaceCurrentPath.textContent = workspaceRoot;
  elements.emptyWorkspacePath.textContent = workspaceRoot;
}

function openWorkspaceDialog() {
  if (!state.document && !state.workspaceRoot) return;
  renderWorkspaceRoot();
  elements.workspacePathInput.value = state.document?.workspaceRoot ?? state.workspaceRoot;
  elements.workspaceDialog.showModal();
  elements.workspacePathInput.focus();
  elements.workspacePathInput.select();
}

function closeWorkspaceDialog() {
  if (elements.workspaceDialog.open) elements.workspaceDialog.close();
}

async function switchWorkspace(path) {
  const value = path.trim();
  if (!value) {
    showToast('Enter a workspace folder.', true);
    return;
  }
  setBusy(true, 'Switching workspace');
  try {
    const result = await api('/api/workspace', {
      method: 'POST',
      body: JSON.stringify({ path: value, headCount: getRequestedHeadCount() }),
    });
    await finishWorkspaceSwitch(result);
  } catch (error) {
    showToast(error.message, true);
    setStatus('Workspace unchanged');
  } finally {
    setBusy(false);
  }
}

async function pickWorkspace() {
  setBusy(true, 'Choosing workspace');
  try {
    const result = await api(
      `/api/workspace/pick?headCount=${encodeURIComponent(getRequestedHeadCount())}`,
      { method: 'POST' }
    );
    if (result.cancelled) {
      setStatus(state.document ? 'Ready' : 'Choose workspace');
      return;
    }
    await finishWorkspaceSwitch(result);
  } catch (error) {
    showToast(error.message, true);
    setStatus('Workspace unchanged');
  } finally {
    setBusy(false);
  }
}

async function finishWorkspaceSwitch(result) {
  const headCount = getRequestedHeadCount();
  clearLinkedAudio();
  closeWorkspaceDialog();
  state.document = null;
  state.workspaceRoot = result.workspaceRoot;
  state.projectRevision = null;
  await loadBootstrap('', headCount);
  showToast(`Workspace switched to ${result.workspaceRoot}`);
}

async function enterEmptyWorkspace() {
  const health = await api('/api/health').catch(() => null);
  state.document = null;
  state.workspaceRoot = health?.workspaceRoot ?? state.workspaceRoot;
  renderWorkspaceRoot();
  setEmptyWorkspace(true);
  elements.workspaceStatus.textContent = 'No sequences found';
  setStatus('Choose workspace');
}

function setEmptyWorkspace(active) {
  elements.workspace.classList.toggle('empty-workspace-active', active);
  elements.emptyWorkspace.hidden = !active;
  elements.sequenceSelect.disabled = active;
  elements.headCountInput.disabled = active;
  elements.regenerateSongButton.disabled = active;
  elements.generateButton.disabled = active;
  elements.projectMenu.hidden = active;
  if (active) {
    elements.undoSuggestionButton.disabled = true;
    elements.sequenceSelect.replaceChildren(new Option('No sequences found', ''));
  }
}

function getRequestedHeadCount() {
  const headCount = Number(elements.headCountInput.value);
  return Number.isInteger(headCount) && headCount >= 2 && headCount <= 12 ? headCount : 4;
}

function populateSequenceSelector() {
  elements.sequenceSelect.replaceChildren(...state.document.sequences.map(sequence => {
    const option = document.createElement('option');
    option.value = sequence.id;
    option.textContent = formatSequenceOptionName(sequence.name);
    option.title = sequence.name;
    option.setAttribute('aria-label', sequence.name);
    option.selected = sequence.id === state.document.sequenceId;
    return option;
  }));
  elements.sequenceSelect.title = state.document.sequences.find(
    sequence => sequence.id === state.document.sequenceId
  )?.name ?? '';
}

function formatSequenceOptionName(name) {
  const maxLength = 32;
  if (name.length <= maxLength) return name;

  const extensionStart = name.lastIndexOf('.');
  const extension = extensionStart > 0 && name.length - extensionStart <= 8
    ? name.slice(extensionStart)
    : '';
  const prefixLength = maxLength - extension.length - 1;
  return `${name.slice(0, prefixLength).trimEnd()}…${extension}`;
}

function populatePatternSelectors() {
  fillSelect(elements.panSelect, state.document.panPatterns);
  fillSelect(elements.tiltSelect, state.document.tiltPatterns);
  fillSelect(elements.dimmerSelect, state.document.dimmerPatterns, formatDimmer);
  fillSelect(elements.rhythmSelect, state.document.rhythmPatterns);
  fillSelect(elements.motionEnergySelect, state.document.motionEnergies);
  fillSelect(elements.motionShapeSelect, state.document.motionShapes);
  fillSelect(elements.motionPhaseSelect, state.document.motionPhases);
  fillSelect(elements.intensityEnvelopeSelect, state.document.intensityEnvelopes);

  const custom = elements.presetSelect.querySelector('option[value=""]');
  elements.presetSelect.replaceChildren(custom, ...Object.keys(state.document.cueSheet.patterns).map(name => {
    const option = document.createElement('option');
    option.value = name;
    option.textContent = name;
    return option;
  }));
}

function populateTimingSources() {
  const sources = state.document.timing.beatSources;
  elements.timingSourceSelect.replaceChildren(...sources.map(source => {
    const option = document.createElement('option');
    option.value = source.trackName;
    option.textContent = `${source.trackName} · ${source.bpm.toFixed(2)} BPM`;
    option.selected = source.trackName === state.timingSourceName;
    return option;
  }));
  if (!sources.some(source => source.trackName === state.timingSourceName)) {
    state.timingSourceName = state.document.timing.defaultBeatSource;
  }
  elements.timingSourceSelect.value = state.timingSourceName ?? '';
  elements.timingSourceSelect.disabled = sources.length === 0;
}

function renderTimingSummary() {
  const source = activeBeatSource();
  elements.timingReadout.textContent = source
    ? `${source.bpm.toFixed(2)} BPM · ${Math.round(source.confidence * 100)}%`
    : 'No timing';
  const media = state.document.media;
  elements.mediaReadout.textContent = state.audioFileName
    ? `Audio · ${state.audioFileName}`
    : media.exists
    ? `Audio · ${media.fileName}`
    : `Relink audio · ${media.fileName ?? 'not declared'}`;
  elements.mediaReadout.classList.toggle('linked', Boolean(state.audioFileName || media.exists));
}

function fillSelect(select, values, formatter = splitWords) {
  select.replaceChildren(...values.map(value => {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = formatter(value);
    return option;
  }));
}

function renderInspector() {
  const section = selectedSection();
  renderAppliedRegeneration();
  const disabled = !section;
  [elements.cueName, elements.cueStart, elements.cueEnd, elements.presetSelect,
    elements.panSelect, elements.tiltSelect, elements.rhythmSelect, elements.dimmerSelect,
    elements.motionEnergySelect, elements.motionShapeSelect, elements.motionPhaseSelect,
    elements.intensityEnvelopeSelect, elements.shutterToggle,
    elements.splitButton, elements.deleteButton]
    .forEach(control => { control.disabled = disabled; });
  if (!section) return;

  const resolved = resolveSection(section);
  elements.cueName.value = section.name;
  elements.cueStart.value = section.startMs;
  elements.cueEnd.value = section.endMs;
  elements.cueStart.disabled = state.selectedIndex === 0;
  elements.cueEnd.disabled = state.selectedIndex === state.document.cueSheet.sections.length - 1;
  renderMovementSource(section);
  elements.panSelect.value = resolved.pan;
  elements.tiltSelect.value = resolved.tilt;
  elements.rhythmSelect.value = resolved.rhythm;
  elements.motionEnergySelect.value = resolved.motionEnergy;
  elements.motionShapeSelect.value = resolved.motionShape;
  elements.motionPhaseSelect.value = resolved.motionPhase;
  elements.panSelect.disabled = rhythmicPan.has(resolved.rhythm);
  elements.tiltSelect.disabled = rhythmicTilt.has(resolved.rhythm);
  elements.dimmerSelect.value = resolved.dimmer;
  elements.intensityEnvelopeSelect.value = resolved.intensityEnvelope;
  elements.shutterToggle.checked = resolved.shutterOpen;
  renderFixtureButtons(section);
  renderCueValidation();
}

function renderMovementSource(section) {
  elements.presetSelect.querySelector(`option[value="${generatedPresetValue}"]`)?.remove();
  const applied = appliedChoiceForSelected();
  if (!applied) {
    elements.presetSelect.value = section.pattern ?? '';
    return;
  }

  const generated = document.createElement('option');
  generated.value = generatedPresetValue;
  generated.textContent = `Generated · ${applied.name}`;
  generated.disabled = true;
  elements.presetSelect.prepend(generated);
  elements.presetSelect.value = generatedPresetValue;
}

function renderFixtureButtons(section) {
  const selected = selectedHeadSet(section);
  elements.fixtureButtons.replaceChildren(...range(1, state.document.headCount).map(number => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = `MH ${number}`;
    button.className = selected.has(number) ? 'active' : '';
    button.setAttribute('aria-pressed', String(selected.has(number)));
    button.addEventListener('click', () => toggleHead(number));
    return button;
  }));
  updateFixtureActivity();
}

function updateFixtureActivity() {
  if (!state.document) return;
  const shutterOpen = state.document.preview.shutterWindows.some(window =>
    window.start <= state.currentMs && window.end > state.currentMs
  );
  const buttons = elements.fixtureButtons.querySelectorAll('button');
  state.document.preview.fixtures.forEach((fixture, index) => {
    const lit = shutterOpen && evaluateTrack(fixture.dimmer, state.currentMs, 0) > 0;
    const button = buttons[index];
    if (!button) return;
    button.classList.toggle('live', lit);
    button.title = `${fixture.name} · ${lit ? 'lit now' : 'dark now'}`;
  });
}

function renderPreviewFixtureButtons() {
  if (!state.document) return;
  const selected = previewSelectedHeadSet();
  elements.previewFixtureButtons.replaceChildren(...range(1, state.document.headCount).map(number => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = `MH ${number}`;
    button.className = selected.has(number) ? 'active' : '';
    button.setAttribute('aria-pressed', String(selected.has(number)));
    button.addEventListener('click', () => togglePreviewHead(number));
    return button;
  }));
}

function renderRotationButtons() {
  const selected = previewSelectedHeadSet();
  const rotations = [...selected].map(number => state.fixtureRotations[number - 1] ?? 0);
  const common = rotations.length > 0 && rotations.every(value => value === rotations[0])
    ? rotations[0]
    : null;
  elements.rotationButtons.querySelectorAll('[data-rotation]').forEach(button => {
    const active = common === Number(button.dataset.rotation);
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
}

function setFixtureRotation(degrees) {
  const selected = previewSelectedHeadSet();
  for (const number of selected) state.fixtureRotations[number - 1] = degrees;
  saveFixtureRotations();
  renderRotationButtons();
  drawStage();
  scheduleProjectSave();
}

function setMountMode(mode) {
  state.mountMode = mode === 'truss' ? 'truss' : 'floor';
  applyMountMode();
  drawStage();
  scheduleProjectSave();
}

function applyMountMode() {
  document.querySelectorAll('[data-mount-mode]').forEach(candidate => {
    const active = candidate.dataset.mountMode === state.mountMode;
    candidate.classList.toggle('active', active);
    candidate.setAttribute('aria-pressed', String(active));
  });
}

function setPreviewSetupOpen(open) {
  elements.previewSetupPopover.hidden = !open;
  elements.previewSetupButton.classList.toggle('active', open);
  elements.previewSetupButton.setAttribute('aria-expanded', String(open));
}

function setPreviewHeads(heads) {
  state.previewHeads = heads;
  renderPreviewFixtureButtons();
  renderRotationButtons();
}

function togglePreviewHead(number) {
  const selected = previewSelectedHeadSet();
  if (selected.has(number)) {
    if (selected.size === 1) return;
    selected.delete(number);
  } else {
    selected.add(number);
  }
  setPreviewHeads(selected.size === state.document.headCount
    ? []
    : [...selected].sort((left, right) => left - right));
}

function previewSelectedHeadSet() {
  if (!state.document || !state.previewHeads.length) {
    return new Set(range(1, state.document?.headCount ?? 0));
  }
  return new Set(state.previewHeads);
}

function loadFixtureRotations(sequenceId, headCount) {
  try {
    const stored = JSON.parse(localStorage.getItem(rotationStorageKey(sequenceId, headCount)) ?? '[]');
    return range(0, headCount - 1).map(index => normalizeRotation(stored[index]));
  } catch {
    return range(0, headCount - 1).map(() => 0);
  }
}

function saveFixtureRotations() {
  localStorage.setItem(
    rotationStorageKey(state.document.sequenceId, state.document.headCount),
    JSON.stringify(state.fixtureRotations)
  );
}

function rotationStorageKey(sequenceId, headCount) {
  return `moving-head-studio:rotation:${sequenceId}:${headCount}`;
}

function normalizeRotation(value) {
  const numeric = Number(value);
  return [0, 90, 180, 270].includes(numeric) ? numeric : 0;
}

async function loadSuggestions() {
  if (!state.document || !selectedSection()) return;
  const revision = ++state.suggestionRevision;
  const cueIndex = state.selectedIndex;
  elements.refreshSuggestionsButton.disabled = true;
  elements.suggestionContext.textContent = 'Ranking choices';
  elements.suggestionChoices.replaceChildren();
  try {
    const result = await api('/api/suggestions', {
      method: 'POST',
      body: JSON.stringify({ editor: createRequest(), cueIndex }),
    });
    if (revision !== state.suggestionRevision || cueIndex !== state.selectedIndex) return;
    state.suggestions = result;
    renderSuggestions();
  } catch (error) {
    if (revision !== state.suggestionRevision) return;
    elements.suggestionContext.textContent = 'Choices unavailable';
    showToast(error.message, true);
  } finally {
    if (revision === state.suggestionRevision) {
      elements.refreshSuggestionsButton.disabled = false;
    }
  }
}

function renderSuggestions() {
  const result = state.suggestions;
  if (!result || result.cueIndex !== state.selectedIndex) return;
  const concurrent = result.concurrentEffects.length
    ? ` · ${result.concurrentEffects.slice(0, 3).join(', ')}`
    : '';
  const prefix = appliedChoiceForSelected() ? 'Alternatives now · ' : '';
  elements.suggestionContext.textContent =
    `${prefix}${result.contextLabel} · ${result.beatCount} beats${concurrent}`;
  elements.suggestionChoices.replaceChildren(...result.choices.map((choice, index) => {
    const row = document.createElement('div');
    row.className = 'suggestion-option';

    const heading = document.createElement('div');
    heading.className = 'suggestion-heading';
    const name = document.createElement('strong');
    name.textContent = `${index + 1}. ${choice.name}`;
    const confidence = document.createElement('span');
    confidence.className = 'confidence-badge';
    confidence.textContent = `${choice.confidence}%`;
    heading.append(name, confidence);

    const pattern = document.createElement('div');
    pattern.className = 'suggestion-pattern';
    pattern.textContent = [
      choice.energy,
      splitWords(choice.pan),
      splitWords(choice.tilt),
      formatDimmer(choice.dimmer),
      splitWords(choice.motionEnergy),
      splitWords(choice.motionShape),
      splitWords(choice.motionPhase),
      splitWords(choice.intensityEnvelope),
    ].join(' · ');
    const rationale = document.createElement('p');
    rationale.textContent = choice.rationale;
    const evidence = document.createElement('small');
    evidence.textContent = choice.evidence.slice(0, 2).join(' · ');
    const apply = document.createElement('button');
    apply.type = 'button';
    apply.className = 'apply-suggestion';
    apply.dataset.suggestionId = choice.id;
    apply.textContent = 'Apply';
    row.append(heading, pattern, rationale, evidence, apply);
    return row;
  }));
}

function appliedChoiceForSelected() {
  return state.appliedRegenerationChoices?.find(
    choice => choice.cueIndex === state.selectedIndex
  );
}

function renderAppliedRegeneration() {
  const applied = appliedChoiceForSelected();
  elements.appliedRegeneration.hidden = !applied;
  elements.appliedRegeneration.replaceChildren();
  if (!applied) return;

  const heading = document.createElement('div');
  heading.className = 'suggestion-heading';
  const name = document.createElement('strong');
  name.textContent = `Applied · ${applied.name}`;
  const confidence = document.createElement('span');
  confidence.className = 'confidence-badge';
  confidence.textContent = `${applied.confidence}%`;
  heading.append(name, confidence);

  const pattern = document.createElement('div');
  pattern.className = 'suggestion-pattern';
  pattern.textContent = [
    applied.energy,
    splitWords(applied.pan),
    splitWords(applied.tilt),
    formatDimmer(applied.dimmer),
    splitWords(applied.rhythm),
    splitWords(applied.motionEnergy),
    splitWords(applied.motionShape),
    splitWords(applied.motionPhase),
    splitWords(applied.intensityEnvelope),
  ].join(' · ');
  const rationale = document.createElement('p');
  rationale.textContent = applied.rationale;
  elements.appliedRegeneration.append(heading, pattern, rationale);
}

async function regenerateWholeSong() {
  const cueCount = state.document?.cueSheet.sections.length ?? 0;
  if (!cueCount || !window.confirm(
    `Regenerate all ${cueCount} cues? You can restore the current cue sheet with Undo.`
  )) return;

  const undo = captureRegenerationUndo('song');
  const outputFileName = elements.outputFileName.value;
  let regenerated = false;
  setBusy(true, 'Regenerating sequence');
  try {
    const result = await api('/api/regenerate-song', {
      method: 'POST',
      body: JSON.stringify({ editor: createRequest() }),
    });
    state.document = result.document;
    state.selectedIndex = Math.min(undo.selectedIndex, result.document.cueSheet.sections.length - 1);
    state.regenerationUndo = undo;
    state.regenerationSummary = summarizeRegeneration(result);
    state.appliedRegenerationChoices = result.appliedChoices;
    state.suggestions = null;
    elements.outputFileName.value = outputFileName || result.document.suggestedOutputFileName;
    renderDocument({ rebuildSelectors: false });
    setStatus('Ready');
    scheduleProjectSave();
    showToast(
      `Regenerated ${result.appliedChoices.length} cues · ${result.averageConfidence}% average confidence`
    );
    regenerated = true;
  } catch (error) {
    setStatus('Regeneration failed');
    showToast(error.message, true);
  } finally {
    setBusy(false);
    elements.undoSuggestionButton.disabled = !state.regenerationUndo;
    renderInspector();
  }
  if (regenerated) loadSuggestions();
}

function summarizeRegeneration(result) {
  const energyOrder = ['Energetic', 'Balanced', 'Restrained', 'Rest'];
  const counts = result.appliedChoices.reduce((summary, choice) => {
    summary[choice.energy] = (summary[choice.energy] ?? 0) + 1;
    return summary;
  }, {});
  return {
    cueCount: result.appliedChoices.length,
    averageConfidence: result.averageConfidence,
    distribution: energyOrder
      .filter(energy => counts[energy])
      .map(energy => `${energy} ${counts[energy]}`),
  };
}

function renderRegenerationSummary() {
  const summary = state.regenerationSummary;
  elements.regenerationSummary.hidden = !summary;
  elements.regenerationSummary.textContent = summary
    ? `${summary.cueCount} cues · ${summary.averageConfidence}% avg · ${summary.distribution.join(' · ')}`
    : '';
}

function captureRegenerationUndo(scope) {
  return {
    scope,
    selectedIndex: state.selectedIndex,
    cueSheet: structuredClone(state.document.cueSheet),
    summary: state.regenerationSummary ? structuredClone(state.regenerationSummary) : null,
    appliedChoices: state.appliedRegenerationChoices
      ? structuredClone(state.appliedRegenerationChoices)
      : null,
  };
}

function toAppliedRegenerationChoice(cueIndex, choice) {
  return {
    cueIndex,
    choiceId: choice.id,
    name: choice.name,
    energy: choice.energy,
    confidence: choice.confidence,
    rationale: choice.rationale,
    evidence: structuredClone(choice.evidence),
    pan: choice.pan,
    tilt: choice.tilt,
    dimmer: choice.dimmer,
    rhythm: choice.rhythm,
    motionEnergy: choice.motionEnergy,
    motionShape: choice.motionShape,
    motionPhase: choice.motionPhase,
    intensityEnvelope: choice.intensityEnvelope,
    shutterOpen: choice.shutterOpen,
  };
}

function applyRegenerationChoice(id) {
  const choice = state.suggestions?.choices.find(candidate => candidate.id === id);
  const section = selectedSection();
  if (!choice || !section) return;
  const undo = captureRegenerationUndo('cue');
  mutateSection(current => {
    current.pattern = null;
    current.pan = choice.pan;
    current.tilt = choice.tilt;
    current.dimmer = choice.dimmer;
    current.rhythm = choice.rhythm;
    current.motionEnergy = choice.motionEnergy;
    current.motionShape = choice.motionShape;
    current.motionPhase = choice.motionPhase;
    current.intensityEnvelope = choice.intensityEnvelope;
    current.shutterOpen = choice.shutterOpen;
    current.panKeys = null;
    current.tiltKeys = null;
    current.dimmerKeys = null;
  });
  state.regenerationUndo = undo;
  state.regenerationSummary = null;
  state.appliedRegenerationChoices = [
    toAppliedRegenerationChoice(state.selectedIndex, choice),
  ];
  elements.undoSuggestionButton.disabled = false;
  renderRegenerationSummary();
  renderAppliedRegeneration();
  renderMovementSource(section);
  showToast(`Applied ${choice.name}`);
}

function undoRegeneration() {
  const undo = state.regenerationUndo;
  if (!undo || !state.document) return;
  state.document.cueSheet = structuredClone(undo.cueSheet);
  state.selectedIndex = Math.min(undo.selectedIndex, state.document.cueSheet.sections.length - 1);
  state.regenerationUndo = null;
  state.regenerationSummary = undo.summary;
  state.appliedRegenerationChoices = undo.appliedChoices;
  state.suggestions = null;
  elements.undoSuggestionButton.disabled = true;
  renderRegenerationSummary();
  renderInspector();
  renderTimeline();
  scheduleCompile();
  showToast(undo.scope === 'song' ? 'Restored previous cue sheet' : 'Restored previous cue');
}

function renderTimeline() {
  const sections = state.document.cueSheet.sections;
  const duration = state.document.preview.durationMs;
  elements.timeline.querySelectorAll('.timeline-cue').forEach(element => element.remove());
  const fragment = document.createDocumentFragment();
  sections.forEach((section, index) => {
    const resolved = resolveSection(section);
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `timeline-cue ${sectionClass(section, resolved)}`;
    button.style.flexBasis = `${Math.max(.45, ((section.endMs - section.startMs) / duration) * 100)}%`;
    button.textContent = section.name;
    button.title = `${section.name} · ${formatTime(section.startMs)}–${formatTime(section.endMs)}`;
    button.dataset.index = index;
    button.classList.toggle('selected', index === state.selectedIndex);
    button.addEventListener('click', () => { void previewCue(index); });
    fragment.appendChild(button);
  });
  elements.timeline.insertBefore(fragment, elements.timelinePlayhead);
  elements.timelineSummary.textContent = `${sections.length} cues · ${formatTime(duration)}`;
  state.activeTimelineIndex = -1;
  cacheTimelineGeometry();
  updateTimelineActivity();
  updateTimelinePlayhead();
  drawTimingGrid();
}

function cacheTimelineGeometry() {
  const buttons = [...elements.timeline.querySelectorAll('.timeline-cue')];
  const sections = state.document?.cueSheet.sections ?? [];
  state.timelineGeometry = buttons.map((button, index) => ({
    startMs: sections[index].startMs,
    endMs: sections[index].endMs,
    startX: button.offsetLeft,
    endX: buttons[index + 1]?.offsetLeft ?? button.offsetLeft + button.offsetWidth,
  }));
  const last = state.timelineGeometry.at(-1);
  state.timelineContentWidth = Math.max(
    elements.timeline.clientWidth,
    (last?.endX ?? elements.timeline.clientWidth) + 12
  );
  elements.timingCanvas.style.width = `${state.timelineContentWidth}px`;
}

function timelineXForTime(timeMs) {
  const geometry = state.timelineGeometry;
  if (!geometry.length) {
    const ratio = state.document?.preview.durationMs
      ? timeMs / state.document.preview.durationMs
      : 0;
    return 12 + (Math.max(0, elements.timeline.clientWidth - 24) * ratio);
  }
  const segment = geometry.find(item => timeMs < item.endMs) ?? geometry.at(-1);
  const progress = Math.max(0, Math.min(
    1,
    (timeMs - segment.startMs) / Math.max(1, segment.endMs - segment.startMs)
  ));
  return segment.startX + ((segment.endX - segment.startX) * progress);
}

function timelineTimeForX(x) {
  const geometry = state.timelineGeometry;
  if (!geometry.length) {
    const ratio = Math.max(0, Math.min(
      1,
      (x - 12) / Math.max(1, elements.timeline.clientWidth - 24)
    ));
    return ratio * (state.document?.preview.durationMs ?? 0);
  }
  const segment = geometry.find(item => x < item.endX) ?? geometry.at(-1);
  const progress = Math.max(0, Math.min(
    1,
    (x - segment.startX) / Math.max(1, segment.endX - segment.startX)
  ));
  return segment.startMs + ((segment.endMs - segment.startMs) * progress);
}

function setLoopSelected(enabled) {
  state.loopSelected = enabled;
  elements.loopButton.classList.toggle('active', enabled);
  elements.loopButton.setAttribute('aria-pressed', String(enabled));
}

async function previewCue(index) {
  const section = state.document?.cueSheet.sections[index];
  if (!section) return;

  const wasPlaying = state.playing;
  state.selectedIndex = index;
  state.currentMs = section.startMs;
  state.lastFrame = performance.now();
  setLoopSelected(true);
  if (state.audioUrl) elements.audioElement.currentTime = state.currentMs / 1000;
  renderInspector();
  renderTimeline();
  syncPlaybackUi();
  loadSuggestions();
  scheduleProjectSave();
  if (!wasPlaying) await startPlayback();
}

function drawTimingGrid() {
  if (!state.document) return;
  const canvas = elements.timingCanvas;
  const bounds = elements.timeline.getBoundingClientRect();
  const ratio = Math.min(window.devicePixelRatio || 1, 2);
  const contentWidth = Math.max(bounds.width, state.timelineContentWidth);
  const width = Math.max(1, Math.round(contentWidth * ratio));
  const height = Math.max(1, Math.round(bounds.height * ratio));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  const context = canvas.getContext('2d');
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, contentWidth, bounds.height);
  const source = activeBeatSource();
  drawWaveform(context, bounds.height);
  if (source) {
    for (const beat of source.beats) {
      const x = timelineXForTime(beat.timeMs);
      context.strokeStyle = beat.isDownbeat ? 'rgba(242,184,75,.5)' : 'rgba(105,210,208,.16)';
      context.lineWidth = beat.isDownbeat ? 1.25 : 1;
      context.beginPath();
      context.moveTo(x, beat.isDownbeat ? 14 : 22);
      context.lineTo(x, bounds.height - 18);
      context.stroke();
    }
  }

  const phrases = timingLabelLane();
  context.font = '9px Aptos, sans-serif';
  context.fillStyle = 'rgba(238,241,237,.58)';
  let lastRight = -Infinity;
  for (const phrase of phrases) {
    const x = timelineXForTime(phrase.startMs);
    const nextX = timelineXForTime(phrase.endMs);
    const available = Math.max(0, nextX - x - 5);
    if (available < 26 || x < lastRight + 5) continue;
    const label = truncateCanvasText(context, phrase.label, available);
    context.fillText(label, x + 2, 11);
    lastRight = x + context.measureText(label).width;
  }
}

function renderValidation() {
  const report = state.document.validation;
  const metrics = report.metrics;
  const issues = report.issues ?? [];
  elements.validationBadge.textContent = issues.length ? `${issues.length} issue${issues.length === 1 ? '' : 's'}` : 'Ready';
  elements.validationBadge.className = `status-pill ${issues.length ? 'bad' : 'good'}`;
  elements.validationStatusButton.textContent = issues.length
    ? `Validation · ${issues.length} issue${issues.length === 1 ? '' : 's'}`
    : 'Validation · Ready';
  elements.validationStatusButton.className = `validation-status ${issues.length ? 'bad' : 'good'}`;
  const entries = [
    [`${metrics.panDynamicPercent.toFixed(1)}%`, 'Pan time moving'],
    [`${metrics.tiltDynamicPercent.toFixed(1)}%`, 'Tilt time moving'],
    [`${metrics.panMinimum}–${metrics.panMaximum}`, 'Pan range · DMX'],
    [`${metrics.tiltMinimum}–${metrics.tiltMaximum}`, 'Tilt range · DMX'],
    [`${metrics.dimmerVisiblePercent.toFixed(1)}%`, 'Visible'],
    [metrics.blackoutWindowCount, 'Blackouts'],
  ];
  elements.validationMetrics.replaceChildren(...entries.map(([value, label]) => {
    const metric = document.createElement('div');
    metric.className = 'metric';
    metric.innerHTML = `<strong>${value}</strong><span>${label}</span>`;
    return metric;
  }));
  renderMovementLimits();
  elements.validationIssues.replaceChildren(...issues.map(issue => {
    const item = document.createElement('li');
    item.textContent = `${issue.code}: ${issue.message}`;
    return item;
  }));
  elements.motionReadout.textContent = `${metrics.panDynamicPercent.toFixed(1)}% pan time`;
  renderCueValidation();
}

function renderMovementLimits() {
  const profile = state.document?.fixtureProfile;
  if (!profile) return;
  elements.panTravelDegreesInput.value = profile.panTravelDegrees ?? 540;
  elements.tiltTravelDegreesInput.value = profile.tiltTravelDegrees ?? 270;
  for (const axis of ['pan', 'tilt']) {
    const minimumInput = elements[`${axis}MinimumInput`];
    const maximumInput = elements[`${axis}MaximumInput`];
    const settings = profile[axis];
    minimumInput.value = settings.minimum;
    minimumInput.min = 0;
    minimumInput.max = 255;
    maximumInput.value = settings.maximum;
    maximumInput.min = 0;
    maximumInput.max = 255;
    renderMovementRangeVisual(axis);
  }
}

function updateMovementSlider(axis, boundary) {
  const minimumInput = elements[`${axis}MinimumInput`];
  const maximumInput = elements[`${axis}MaximumInput`];
  let minimum = Number(minimumInput.value);
  let maximum = Number(maximumInput.value);
  if (boundary === 'minimum' && minimum >= maximum) {
    minimum = maximum - 1;
    minimumInput.value = minimum;
  } else if (boundary === 'maximum' && maximum <= minimum) {
    maximum = minimum + 1;
    maximumInput.value = maximum;
  }
  renderMovementRangeVisual(axis);
}

function renderMovementRangeVisual(axis) {
  const minimumText = elements[`${axis}MinimumInput`].value;
  const maximumText = elements[`${axis}MaximumInput`].value;
  const minimum = Number(minimumText);
  const maximum = Number(maximumText);
  const valid = minimumText !== '' && maximumText !== '' &&
    Number.isInteger(minimum) && Number.isInteger(maximum) &&
    minimum >= 0 && maximum <= 255 && minimum < maximum;
  const boundedMinimum = Math.max(0, Math.min(255, Number.isFinite(minimum) ? minimum : 0));
  const boundedMaximum = Math.max(0, Math.min(255, Number.isFinite(maximum) ? maximum : 255));
  const profile = state.document?.fixtureProfile;
  const settings = profile?.[axis] ?? { inverted: false };
  const travel = Number(elements[`${axis}TravelDegreesInput`].value) ||
    profile?.[`${axis}TravelDegrees`] || (axis === 'pan' ? 540 : 270);
  const degreeForValue = value => {
    const normalized = settings.inverted ? 1 - (value / 255) : value / 255;
    return axis === 'pan' ? (normalized - .5) * travel : normalized * travel;
  };
  const minimumDegrees = degreeForValue(boundedMinimum);
  const maximumDegrees = degreeForValue(boundedMaximum);
  const visual = elements[`${axis}RangeVisual`];
  visual.classList.toggle('invalid', !valid);
  const description = valid
    ? `${capitalize(axis)} allowed output range: ${minimum} DMX (${formatDegrees(minimumDegrees)}) ` +
      `to ${maximum} DMX (${formatDegrees(maximumDegrees)})`
    : `Invalid ${axis} output range`;
  visual.title = description;
  visual.setAttribute('aria-label', description);
  elements[`${axis}MinimumOutput`].textContent =
    `MIN · ${boundedMinimum} DMX · ${formatDegrees(minimumDegrees)}`;
  elements[`${axis}MaximumOutput`].textContent =
    `MAX · ${boundedMaximum} DMX · ${formatDegrees(maximumDegrees)}`;
  elements[`${axis}MinimumInput`].setAttribute(
    'aria-valuetext',
    `${boundedMinimum} DMX, ${formatDegrees(minimumDegrees)}`
  );
  elements[`${axis}MaximumInput`].setAttribute(
    'aria-valuetext',
    `${boundedMaximum} DMX, ${formatDegrees(maximumDegrees)}`
  );
  renderAxisDirectionLabels(axis, settings.inverted);
  const sliderStart = Math.min(boundedMinimum, boundedMaximum) / 255 * 100;
  const sliderEnd = Math.max(boundedMinimum, boundedMaximum) / 255 * 100;
  const sliderSpan = sliderEnd - sliderStart;
  const thumbDiameter = 16;
  const sliderWindow = elements[`${axis}SliderWindow`];
  sliderWindow.style.left =
    `calc(${sliderStart}% + ${thumbDiameter / 2 - (sliderStart / 100 * thumbDiameter)}px)`;
  sliderWindow.style.width =
    `calc(${sliderSpan}% - ${sliderSpan / 100 * thumbDiameter}px)`;
  const center = { x: 110, y: 78 };
  const radius = 62;
  const pointForValue = value => {
    const normalized = value / 255;
    const angle = Math.PI - (normalized * Math.PI);
    return {
      x: center.x + (Math.cos(angle) * radius),
      y: center.y - (Math.sin(angle) * radius),
    };
  };
  const lowPoint = pointForValue(Math.min(boundedMinimum, boundedMaximum));
  const highPoint = pointForValue(Math.max(boundedMinimum, boundedMaximum));
  const sweep = 1;
  elements[`${axis}RangeWindow`].setAttribute(
    'd',
    `M ${center.x} ${center.y} L ${lowPoint.x} ${lowPoint.y} ` +
      `A ${radius} ${radius} 0 0 ${sweep} ${highPoint.x} ${highPoint.y} Z`
  );
  setRangeMarker(elements[`${axis}MinimumMarker`], center, pointForValue(boundedMinimum));
  setRangeMarker(elements[`${axis}MaximumMarker`], center, pointForValue(boundedMaximum));
}

function renderAxisDirectionLabels(axis, inverted) {
  if (axis === 'pan') {
    elements.panLowDirectionLabel.textContent = inverted ? 'R · 0' : 'L · 0';
    elements.panHighDirectionLabel.textContent = inverted ? '255 · L' : '255 · R';
    return;
  }
  elements.tiltLowDirectionLabel.textContent = inverted ? '0 · BACK' : '0 · FORWARD';
  elements.tiltHighDirectionLabel.textContent = inverted ? 'FORWARD · 255' : 'BACK · 255';
}

function formatDegrees(value) {
  const normalized = Math.abs(value) < .05 ? 0 : value;
  const prefix = normalized > 0 ? '+' : normalized < 0 ? '−' : '';
  return `${prefix}${Math.abs(normalized).toFixed(1)}°`;
}

function setRangeMarker(marker, center, point) {
  marker.setAttribute('x1', center.x);
  marker.setAttribute('y1', center.y);
  marker.setAttribute('x2', point.x);
  marker.setAttribute('y2', point.y);
}

function updateMovementLimits(axis) {
  if (!state.document) return;
  const minimum = Number(elements[`${axis}MinimumInput`].value);
  const maximum = Number(elements[`${axis}MaximumInput`].value);
  if (!Number.isInteger(minimum) || !Number.isInteger(maximum) ||
      minimum < 0 || maximum > 255 || minimum >= maximum) {
    showToast(`${capitalize(axis)} boundaries must be whole numbers from 0 to 255 with minimum below maximum.`, true);
    renderMovementLimits();
    return;
  }

  const profile = state.document.fixtureProfile;
  profile[axis] = constrainAxis(profile[axis], minimum, maximum);
  for (const fixture of Object.values(profile.fixtures ?? {})) {
    if (fixture[axis]) fixture[axis] = constrainAxis(fixture[axis], minimum, maximum);
  }

  state.regenerationUndo = null;
  elements.undoSuggestionButton.disabled = true;
  clearTimeout(state.projectSaveTimer);
  elements.workspaceStatus.textContent = 'Unsaved changes';
  renderMovementLimits();
  scheduleCompile();
}

function updateTravelDegrees(axis) {
  if (!state.document) return;
  const input = elements[`${axis}TravelDegreesInput`];
  const value = Number(input.value);
  if (!Number.isInteger(value) || value < 1 || value > 1080) {
    showToast(`${capitalize(axis)} travel must be a whole number from 1 to 1080 degrees.`, true);
    input.value = state.document.fixtureProfile[`${axis}TravelDegrees`] ??
      (axis === 'pan' ? 540 : 270);
    renderMovementRangeVisual(axis);
    return;
  }
  state.document.fixtureProfile[`${axis}TravelDegrees`] = value;
  renderMovementRangeVisual(axis);
  scheduleProjectSave();
}

function constrainAxis(settings, minimum, maximum) {
  const clamp = value => Math.max(minimum, Math.min(maximum, value));
  return {
    ...settings,
    minimum,
    maximum,
    park: clamp(settings.park),
    outer: clamp(settings.outer),
    center: clamp(settings.center),
  };
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function renderCueValidation() {
  const section = selectedSection();
  const issues = state.document?.validation?.issues ?? [];
  if (!section) {
    elements.cueValidationSummary.hidden = true;
    return;
  }
  const cueIssues = issues.filter(issue => {
    if (issue.start == null) return false;
    const issueEnd = issue.end ?? issue.start;
    return issue.start < section.endMs && issueEnd >= section.startMs;
  });
  elements.cueValidationSummary.hidden = cueIssues.length === 0;
  elements.cueValidationSummary.textContent = cueIssues.length
    ? `${cueIssues.length} cue issue${cueIssues.length === 1 ? '' : 's'} · ` +
      `${cueIssues[0].code}: ${cueIssues[0].message}`
    : '';
}

function setValidationDrawerOpen(open) {
  elements.validationDrawer.hidden = !open;
  elements.validationDrawerBackdrop.hidden = !open;
  elements.validationStatusButton.setAttribute('aria-expanded', String(open));
  if (open) elements.validationDrawerCloseButton.focus();
}

function syncPlaybackUi() {
  if (!state.document) return;
  state.currentMs = Math.max(0, Math.min(state.currentMs, state.document.preview.durationMs));
  elements.playhead.value = state.currentMs;
  elements.playButton.textContent = state.playing ? 'Ⅱ' : '▶';
  elements.playButton.title = state.playing ? 'Pause' : 'Play';
  elements.timeDisplay.textContent = formatTime(state.currentMs);
  const active = activeSection();
  const phrase = activePhrase();
  elements.sectionReadout.textContent = [active?.name ?? 'End', phrase?.label]
    .filter(Boolean)
    .join(' · ');
  updateTimelineActivity();
  updateTimelinePlayhead();
  updateFixtureActivity();
  drawStage();
}

function updateTimelineActivity() {
  const activeIndex = state.document.cueSheet.sections.findIndex(section =>
    section.startMs <= state.currentMs && section.endMs > state.currentMs
  );
  if (activeIndex === state.activeTimelineIndex) return;
  const buttons = elements.timeline.querySelectorAll('.timeline-cue');
  if (state.activeTimelineIndex >= 0) {
    buttons[state.activeTimelineIndex]?.classList.remove('active');
  }
  if (activeIndex >= 0) buttons[activeIndex]?.classList.add('active');
  state.activeTimelineIndex = activeIndex;
}

function updateTimelinePlayhead() {
  const x = timelineXForTime(state.currentMs);
  elements.timelinePlayhead.style.transform = `translate3d(${x}px, 0, 0)`;
}

async function togglePlayback() {
  if (!state.document) return;
  if (state.playing) {
    state.playing = false;
    elements.audioElement.pause();
    syncPlaybackUi();
    return;
  }
  setLoopSelected(false);
  await startPlayback();
}

async function startPlayback() {
  if (!state.document || state.playing) return;
  if (state.currentMs >= state.document.preview.durationMs) state.currentMs = 0;
  if (state.audioUrl) {
    elements.audioElement.currentTime = state.currentMs / 1000;
    try {
      await elements.audioElement.play();
    } catch (error) {
      showToast(`Audio playback failed: ${error.message}`, true);
      return;
    }
  }
  state.playing = true;
  state.lastFrame = performance.now();
  syncPlaybackUi();
  requestAnimationFrame(playbackFrame);
}

function playbackFrame(now) {
  if (!state.playing) return;
  state.currentMs = state.audioUrl
    ? elements.audioElement.currentTime * 1000
    : state.currentMs + (now - state.lastFrame);
  state.lastFrame = now;
  const loop = state.loopSelected ? selectedSection() : null;
  if (loop && state.currentMs >= loop.endMs) {
    const duration = Math.max(1, loop.endMs - loop.startMs);
    state.currentMs = loop.startMs + ((state.currentMs - loop.startMs) % duration);
    if (state.audioUrl) elements.audioElement.currentTime = state.currentMs / 1000;
  } else if (state.currentMs >= state.document.preview.durationMs) {
    state.currentMs = state.document.preview.durationMs;
    state.playing = false;
    elements.audioElement.pause();
  }
  syncPlaybackUi();
  if (state.playing) requestAnimationFrame(playbackFrame);
}

function drawStage() {
  if (!state.document) return;
  const canvas = elements.stageCanvas;
  const bounds = canvas.getBoundingClientRect();
  const ratio = Math.min(window.devicePixelRatio || 1, 2);
  const width = Math.max(1, Math.round(bounds.width * ratio));
  const height = Math.max(1, Math.round(bounds.height * ratio));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  const context = canvas.getContext('2d');
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  const w = bounds.width;
  const h = bounds.height;
  context.clearRect(0, 0, w, h);
  drawStageGrid(context, w, h);

  const fixtures = state.document.preview.fixtures;
  const shutterOpen = state.document.preview.shutterWindows.some(window =>
    window.start <= state.currentMs && window.end > state.currentMs
  );
  const margin = Math.max(52, w * .09);
  const fixtureSpacing = fixtures.length > 1
    ? (w - (2 * margin)) / (fixtures.length - 1)
    : 48;
  const fixtureScale = Math.max(.55, Math.min(1, fixtureSpacing / 44));
  const floorMounted = state.mountMode === 'floor';
  const bodyY = floorMounted ? h * .79 : Math.max(72, h * .18);
  const selected = selectedHeadSet(selectedSection());

  fixtures.forEach((fixture, index) => {
    const x = fixtures.length === 1
      ? w / 2
      : margin + (index * (w - (2 * margin)) / (fixtures.length - 1));
    const pan = evaluateTrack(fixture.pan, state.currentMs, fixture.panRange.park);
    const tilt = evaluateTrack(fixture.tilt, state.currentMs, fixture.tiltRange.park);
    const dimmer = evaluateTrack(fixture.dimmer, state.currentMs, 0);
    const rotation = state.fixtureRotations[index] ?? 0;
    drawBeam(
      context,
      x,
      bodyY,
      w,
      h,
      fixture,
      pan,
      tilt,
      dimmer,
      shutterOpen,
      state.mountMode,
      rotation
    );
  });

  if (floorMounted) {
    drawFloor(context, margin, w - margin, bodyY + 21);
  } else {
    drawTruss(context, margin, w - margin, bodyY - 28);
  }
  fixtures.forEach((fixture, index) => {
    const x = fixtures.length === 1
      ? w / 2
      : margin + (index * (w - (2 * margin)) / (fixtures.length - 1));
    const pan = evaluateTrack(fixture.pan, state.currentMs, fixture.panRange.park);
    drawFixture(
      context,
      x,
      bodyY,
      fixture.number,
      pan,
      fixture.panRange,
      selected.has(fixture.number),
      state.mountMode,
      fixtureScale,
      state.fixtureRotations[index] ?? 0
    );
  });
}

function drawStageGrid(context, width, height) {
  const horizon = height * .54;
  context.save();
  context.strokeStyle = 'rgba(122, 151, 143, .12)';
  context.lineWidth = 1;
  for (let row = 0; row < 6; row++) {
    const y = horizon + ((height - horizon) * (row / 5) ** 1.6);
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(width, y);
    context.stroke();
  }
  for (let column = -5; column <= 5; column++) {
    context.beginPath();
    context.moveTo(width / 2, horizon);
    context.lineTo((width / 2) + column * width * .16, height);
    context.stroke();
  }
  context.restore();
}

function drawTruss(context, startX, endX, y) {
  context.save();
  context.strokeStyle = '#46504e';
  context.lineWidth = 3;
  context.beginPath();
  context.moveTo(startX - 24, y);
  context.lineTo(endX + 24, y);
  context.stroke();
  context.strokeStyle = '#252c2c';
  context.lineWidth = 1;
  for (let x = startX - 20; x < endX + 20; x += 22) {
    context.beginPath();
    context.moveTo(x, y - 6);
    context.lineTo(x + 18, y + 6);
    context.stroke();
  }
  context.restore();
}

function drawFloor(context, startX, endX, y) {
  context.save();
  const gradient = context.createLinearGradient(startX, y, endX, y);
  gradient.addColorStop(0, 'rgba(70,80,78,0)');
  gradient.addColorStop(.15, '#46504e');
  gradient.addColorStop(.85, '#46504e');
  gradient.addColorStop(1, 'rgba(70,80,78,0)');
  context.strokeStyle = gradient;
  context.lineWidth = 3;
  context.beginPath();
  context.moveTo(startX - 28, y);
  context.lineTo(endX + 28, y);
  context.stroke();
  context.restore();
}

function drawBeam(
  context,
  x,
  y,
  width,
  height,
  fixture,
  pan,
  tilt,
  dimmer,
  shutterOpen,
  mountMode,
  rotation
) {
  if (!shutterOpen || dimmer <= 0) return;
  const panSpan = Math.max(1, fixture.panRange.maximum - fixture.panRange.minimum);
  const tiltSpan = Math.max(1, fixture.tiltRange.maximum - fixture.tiltRange.minimum);
  const panOffset = (pan - fixture.panRange.park) / panSpan;
  const tiltLevel = Math.max(0, Math.min(1, (tilt - fixture.tiltRange.minimum) / tiltSpan));
  const floorMounted = mountMode === 'floor';
  const originY = floorMounted ? y - 10 : y + 8;
  const targetY = floorMounted
    ? height * (.38 - (tiltLevel * .25))
    : height * (.91 - (tiltLevel * .23));
  const yaw = fixtureYaw(panOffset, rotation);
  const horizontalReach = width * .88;
  const targetX = x + (Math.sin(yaw) * horizontalReach);
  const intensity = Math.max(0, Math.min(1, dimmer / 100));
  const spread = 5 + ((1 - tiltLevel) * 2);
  const gradient = context.createLinearGradient(x, y, targetX, targetY);
  gradient.addColorStop(0, `rgba(255, 255, 255, ${.92 * intensity})`);
  gradient.addColorStop(.78, `rgba(255, 255, 255, ${.62 * intensity})`);
  gradient.addColorStop(1, 'rgba(255, 255, 255, 0)');
  context.save();
  context.globalCompositeOperation = 'lighter';
  context.fillStyle = gradient;
  context.beginPath();
  context.moveTo(x - 2, originY);
  context.lineTo(targetX - spread, targetY);
  context.lineTo(targetX + spread, targetY);
  context.lineTo(x + 2, originY);
  context.closePath();
  context.fill();
  context.strokeStyle = `rgba(255, 255, 255, ${.82 * intensity})`;
  context.lineWidth = 1.5;
  context.beginPath();
  context.moveTo(x, originY);
  context.lineTo(targetX, targetY);
  context.stroke();
  context.fillStyle = `rgba(255, 255, 255, ${.32 * intensity})`;
  context.beginPath();
  context.ellipse(targetX, targetY, spread * 1.15, spread * .32, 0, 0, Math.PI * 2);
  context.fill();
  context.restore();
}

function drawFixture(context, x, y, number, pan, panRange, selected, mountMode, scale, rotation) {
  const normalizedPan = (pan - panRange.park) / Math.max(1, panRange.maximum - panRange.minimum);
  const yaw = fixtureYaw(normalizedPan, rotation);
  context.save();
  context.translate(x, y);
  context.scale(scale, scale);
  if (mountMode === 'truss') context.scale(1, -1);
  context.fillStyle = selected ? '#f2b84b' : '#899491';
  context.strokeStyle = selected ? '#ffe0a0' : '#c0c8c5';
  context.lineWidth = selected ? 2 : 1;
  context.beginPath();
  context.roundRect(-15, -12, 30, 23, 5);
  context.fill();
  context.stroke();
  context.fillStyle = '#151a1b';
  context.fillRect(-19, 11, 38, 7);
  const face = Math.cos(yaw);
  const side = Math.sin(yaw);
  const headWidth = 9 + (11 * Math.abs(face));
  const headX = side * 4;
  context.fillStyle = '#272e2e';
  context.strokeStyle = '#dfe6e2';
  context.beginPath();
  context.roundRect(headX - (headWidth / 2), -18, headWidth, 14, 4);
  context.fill();
  context.stroke();
  context.fillStyle = face >= 0
    ? (selected ? '#69d2d0' : '#687573')
    : '#121718';
  context.beginPath();
  context.ellipse(
    headX + (side * headWidth * .28),
    -11,
    Math.max(1.5, 4 * Math.abs(face)),
    4,
    0,
    0,
    Math.PI * 2
  );
  context.fill();
  context.strokeStyle = selected ? '#ffe0a0' : '#687573';
  context.beginPath();
  context.moveTo(headX, -11);
  context.lineTo(headX + (side * 11), -11);
  context.stroke();
  context.restore();

  context.save();
  context.fillStyle = 'rgba(238,241,237,.65)';
  context.font = `${Math.max(7, 10 * scale)}px Cascadia Mono, monospace`;
  context.textAlign = 'center';
  context.fillText(scale < .75 ? String(number) : `MH ${number}`, x, y + 18 + (16 * scale));
  context.restore();
}

function fixtureYaw(normalizedPan, rotation) {
  return (rotation * Math.PI / 180) + (normalizedPan * 1.7);
}

function evaluateTrack(segments, timeMs, fallback) {
  const segment = segments.find(item => item.startMs <= timeMs && item.endMs > timeMs)
    ?? segments.at(-1);
  if (!segment) return fallback;
  if (segment.kind === 'Hold' || segment.endMs <= segment.startMs) return segment.startValue;
  const progress = Math.max(0, Math.min(1, (timeMs - segment.startMs) / (segment.endMs - segment.startMs)));
  if (segment.kind === 'Linear') return lerp(segment.startValue, segment.endValue, progress);
  if (segment.kind === 'Bounce') {
    return progress <= .5
      ? lerp(segment.startValue, segment.middleValue ?? segment.endValue, progress * 2)
      : lerp(segment.middleValue ?? segment.startValue, segment.endValue, (progress - .5) * 2);
  }
  if (segment.kind === 'Custom' && segment.points?.length) {
    const rightIndex = segment.points.findIndex(point => progress <= point.time);
    if (rightIndex <= 0) return segment.points[0].value;
    const left = segment.points[rightIndex - 1];
    const right = segment.points[rightIndex];
    const span = right.time - left.time;
    return span <= 0 ? right.value : lerp(left.value, right.value, (progress - left.time) / span);
  }
  return segment.startValue;
}

function setCustomProperty(property, value) {
  mutateSection(section => {
    const resolved = resolveSection(section);
    section.pattern = null;
    section.pan = resolved.pan;
    section.tilt = resolved.tilt;
    section.dimmer = resolved.dimmer;
    section.rhythm = resolved.rhythm;
    section.motionEnergy = resolved.motionEnergy;
    section.motionShape = resolved.motionShape;
    section.motionPhase = resolved.motionPhase;
    section.intensityEnvelope = resolved.intensityEnvelope;
    section.shutterOpen = resolved.shutterOpen;
    section[property] = value;
    if (property === 'pan') section.panKeys = null;
    if (property === 'tilt') section.tiltKeys = null;
    if (property === 'dimmer') section.dimmerKeys = null;
    if (['motionEnergy', 'motionShape', 'motionPhase'].includes(property)) {
      section.panKeys = null;
      section.tiltKeys = null;
    }
  });
}

function setRhythmPattern() {
  const rhythm = elements.rhythmSelect.value;
  mutateSection(section => {
    section.rhythm = rhythm;
    if (rhythmicPan.has(rhythm)) {
      section.pan = 'Fan';
      section.panKeys = null;
    }
    if (rhythmicTilt.has(rhythm)) {
      section.tilt = 'High';
      section.tiltKeys = null;
    }
  });
}

function applyPreset() {
  const preset = elements.presetSelect.value;
  if (preset === generatedPresetValue) return;
  mutateSection(section => {
    if (!preset) {
      const resolved = resolveSection(section);
      section.pattern = null;
      section.pan = resolved.pan;
      section.tilt = resolved.tilt;
      section.dimmer = resolved.dimmer;
      section.rhythm = resolved.rhythm;
      section.motionEnergy = resolved.motionEnergy;
      section.motionShape = resolved.motionShape;
      section.motionPhase = resolved.motionPhase;
      section.intensityEnvelope = resolved.intensityEnvelope;
      section.shutterOpen = resolved.shutterOpen;
      return;
    }
    section.pattern = preset;
    section.pan = null;
    section.tilt = null;
    section.dimmer = null;
    section.rhythm = null;
    section.motionEnergy = null;
    section.motionShape = null;
    section.motionPhase = null;
    section.intensityEnvelope = null;
    section.shutterOpen = null;
    section.panKeys = null;
    section.tiltKeys = null;
    section.dimmerKeys = null;
  });
}

function setHeads(heads) {
  mutateSection(section => { section.heads = heads; });
}

function toggleHead(number) {
  const section = selectedSection();
  const selected = selectedHeadSet(section);
  if (selected.has(number)) {
    if (selected.size === 1) return;
    selected.delete(number);
  } else {
    selected.add(number);
  }
  const all = selected.size === state.document.headCount;
  setHeads(all ? [] : [...selected].sort((a, b) => a - b));
}

function selectedHeadSet(section) {
  if (!state.document || !section || !section.heads?.length) {
    return new Set(range(1, state.document?.headCount ?? 0));
  }
  return new Set(section.heads);
}

function updateStartBoundary() {
  const index = state.selectedIndex;
  if (index <= 0) return;
  const sections = state.document.cueSheet.sections;
  const minimum = sections[index - 1].startMs + 25;
  const maximum = sections[index].endMs - 25;
  const boundary = Math.max(minimum, Math.min(maximum, snapEditorTime(Number(elements.cueStart.value))));
  sections[index - 1].endMs = boundary;
  sections[index].startMs = boundary;
  scheduleCompile();
  renderTimeline();
  renderInspector();
}

function updateEndBoundary() {
  const index = state.selectedIndex;
  const sections = state.document.cueSheet.sections;
  if (index >= sections.length - 1) return;
  const minimum = sections[index].startMs + 25;
  const maximum = sections[index + 1].endMs - 25;
  const boundary = Math.max(minimum, Math.min(maximum, snapEditorTime(Number(elements.cueEnd.value))));
  sections[index].endMs = boundary;
  sections[index + 1].startMs = boundary;
  scheduleCompile();
  renderTimeline();
  renderInspector();
}

function splitSection() {
  const section = selectedSection();
  const split = snapEditorTime(state.currentMs);
  if (!section || split <= section.startMs || split >= section.endMs) {
    showToast('Move the playhead inside the selected cue before splitting.', true);
    return;
  }
  const originalStart = section.startMs;
  const originalEnd = section.endMs;
  const second = structuredClone(section);
  section.endMs = split;
  second.startMs = split;
  second.name = `${section.name} B`;
  for (const property of ['panKeys', 'tiltKeys', 'dimmerKeys']) {
    if (!section[property]?.length) continue;
    const originalKeys = [...section[property]];
    section[property] = originalKeys.map(key =>
      sliceCurveKey(key, originalStart, originalEnd, originalStart, split)
    );
    second[property] = originalKeys.map(key =>
      sliceCurveKey(key, originalStart, originalEnd, split, originalEnd)
    );
  }
  state.appliedRegenerationChoices = null;
  state.regenerationSummary = null;
  renderRegenerationSummary();
  state.document.cueSheet.sections.splice(state.selectedIndex + 1, 0, second);
  state.selectedIndex += 1;
  scheduleCompile();
  renderInspector();
  renderTimeline();
}

function deleteSection() {
  const sections = state.document.cueSheet.sections;
  if (sections.length <= 1) return;
  state.appliedRegenerationChoices = null;
  state.regenerationSummary = null;
  renderRegenerationSummary();
  const index = state.selectedIndex;
  if (index === 0) {
    sections[1].startMs = 0;
    sections.splice(0, 1);
  } else {
    sections[index - 1].endMs = sections[index].endMs;
    sections.splice(index, 1);
    state.selectedIndex -= 1;
  }
  scheduleCompile();
  renderInspector();
  renderTimeline();
}

function mutateSection(mutator, { compile = true } = {}) {
  const section = selectedSection();
  if (!section) return;
  clearTimeout(state.projectSaveTimer);
  elements.workspaceStatus.textContent = 'Unsaved changes';
  if (state.appliedRegenerationChoices) {
    state.appliedRegenerationChoices = state.appliedRegenerationChoices.filter(
      choice => choice.cueIndex !== state.selectedIndex
    );
    state.regenerationSummary = null;
    renderRegenerationSummary();
  }
  mutator(section);
  renderInspector();
  renderTimeline();
  if (compile) scheduleCompile();
}

function resolveSection(section) {
  const defaults = state.document.cueSheet.defaults ?? {};
  const pattern = section.pattern ? state.document.cueSheet.patterns[section.pattern] ?? {} : {};
  return {
    pan: section.pan ?? pattern.pan ?? defaults.pan,
    tilt: section.tilt ?? pattern.tilt ?? defaults.tilt,
    dimmer: section.dimmer ?? pattern.dimmer ?? defaults.dimmer,
    rhythm: section.rhythm ?? pattern.rhythm ?? defaults.rhythm ?? 'Off',
    motionEnergy: section.motionEnergy ?? pattern.motionEnergy ?? defaults.motionEnergy ?? 'Full',
    motionShape: section.motionShape ?? pattern.motionShape ?? defaults.motionShape ?? 'Smooth',
    motionPhase: section.motionPhase ?? pattern.motionPhase ?? defaults.motionPhase ?? 'Together',
    intensityEnvelope: section.intensityEnvelope ?? pattern.intensityEnvelope ??
      defaults.intensityEnvelope ?? 'Steady',
    shutterOpen: section.shutterOpen ?? pattern.shutterOpen ?? defaults.shutterOpen,
  };
}

function sectionClass(section, resolved) {
  const explicitVisible = section.dimmerKeys?.some(key =>
    [...String(key).matchAll(/\d+(?:\.\d+)?/g)].some(match => Number(match[0]) > 0)
  );
  if (!resolved.shutterOpen || explicitVisible === false ||
      (explicitVisible == null && resolved.dimmer === 'All0')) return 'dark';
  if (resolved.rhythm !== 'Off') return 'motion';
  if (section.panKeys?.some(key => key.includes('_')) ||
      section.tiltKeys?.some(key => key.includes('_'))) return 'motion';
  if (dynamicPan.has(resolved.pan) || dynamicTilt.has(resolved.tilt)) return 'motion';
  return 'hold';
}

function activeSection() {
  return state.document?.cueSheet.sections.find(section =>
    section.startMs <= state.currentMs && section.endMs > state.currentMs
  );
}

function selectedSection() {
  return state.document?.cueSheet.sections[state.selectedIndex];
}

function activeBeatSource() {
  return state.document?.timing.beatSources.find(source => source.trackName === state.timingSourceName)
    ?? state.document?.timing.beatSources[0];
}

async function linkAudio(event) {
  const file = event.target.files?.[0];
  event.target.value = '';
  if (!file) return;
  setStatus('Reading audio');
  try {
    clearLinkedAudio();
    state.audioUrl = URL.createObjectURL(file);
    state.audioFileName = file.name;
    elements.audioElement.src = state.audioUrl;
    elements.audioElement.volume = Number(elements.volumeSlider.value);
    await waitForAudioMetadata(elements.audioElement);

    const audioContext = new AudioContext();
    const audioBuffer = await audioContext.decodeAudioData(await file.arrayBuffer());
    const channels = Array.from(
      { length: audioBuffer.numberOfChannels },
      (_, index) => audioBuffer.getChannelData(index).slice()
    );
    state.waveformPeaks = await computeWaveform(channels, 4096);
    await audioContext.close();
    renderTimingSummary();
    drawTimingGrid();

    const difference = Math.abs((audioBuffer.duration * 1000) - state.document.preview.durationMs);
    if (difference > 1000) {
      showToast(`Audio duration differs from the sequence by ${(difference / 1000).toFixed(1)} seconds.`, true);
    } else {
      showToast(`Linked ${file.name}`);
    }
    setStatus('Ready');
  } catch (error) {
    clearLinkedAudio();
    renderTimingSummary();
    setStatus('Audio link failed');
    showToast(`Could not decode audio: ${error.message}`, true);
  }
}

function clearLinkedAudio() {
  state.playing = false;
  elements.audioElement.pause();
  elements.audioElement.removeAttribute('src');
  elements.audioElement.load();
  if (state.audioUrl) URL.revokeObjectURL(state.audioUrl);
  state.audioUrl = null;
  state.audioFileName = null;
  state.waveformPeaks = null;
}

function waitForAudioMetadata(audio) {
  if (Number.isFinite(audio.duration)) return Promise.resolve();
  return new Promise((resolve, reject) => {
    audio.addEventListener('loadedmetadata', resolve, { once: true });
    audio.addEventListener('error', () => reject(new Error('Unsupported or unreadable audio file.')), { once: true });
  });
}

function computeWaveform(channels, bins) {
  return new Promise((resolve, reject) => {
    const worker = new Worker('waveform-worker.js');
    worker.addEventListener('message', event => {
      worker.terminate();
      resolve(event.data.peaks);
    }, { once: true });
    worker.addEventListener('error', event => {
      worker.terminate();
      reject(new Error(event.message || 'Waveform worker failed.'));
    }, { once: true });
    worker.postMessage({ channels, bins }, channels.map(channel => channel.buffer));
  });
}

function drawWaveform(context, height) {
  const peaks = state.waveformPeaks;
  if (!peaks?.length) return;
  const center = height * .56;
  const amplitude = height * .28;
  const duration = state.document.preview.durationMs;

  context.save();
  context.fillStyle = 'rgba(105, 210, 208, .14)';
  context.beginPath();
  peaks.forEach((peak, index) => {
    const x = timelineXForTime((index / (peaks.length - 1)) * duration);
    const y = center + (peak.min * amplitude);
    if (index === 0) context.moveTo(x, y); else context.lineTo(x, y);
  });
  for (let index = peaks.length - 1; index >= 0; index--) {
    const peak = peaks[index];
    const x = timelineXForTime((index / (peaks.length - 1)) * duration);
    context.lineTo(x, center + (peak.max * amplitude));
  }
  context.closePath();
  context.fill();

  context.strokeStyle = 'rgba(238, 241, 237, .28)';
  context.beginPath();
  peaks.forEach((peak, index) => {
    const x = timelineXForTime((index / (peaks.length - 1)) * duration);
    const y = center - (peak.rms * amplitude);
    if (index === 0) context.moveTo(x, y); else context.lineTo(x, y);
  });
  context.stroke();
  context.restore();
}

function timingLabelLane() {
  const phrases = state.document?.timing.phrases ?? [];
  const sections = phrases.filter(marker => marker.kind === 'Section');
  if (sections.length) return sections;
  const trackName = phrases[0]?.trackName;
  return trackName ? phrases.filter(marker => marker.trackName === trackName) : [];
}

function activePhrase() {
  const matching = (state.document?.timing.phrases ?? [])
    .filter(marker => marker.startMs <= state.currentMs && marker.endMs > state.currentMs)
    .sort((left, right) => {
      if (left.kind !== right.kind) return left.kind === 'Section' ? -1 : 1;
      return (left.endMs - left.startMs) - (right.endMs - right.startMs);
    });
  return matching[0];
}

function snapEditorTime(value) {
  const mode = elements.snapSelect.value;
  if (mode === 'off') return snap25(value);
  const source = activeBeatSource();
  if (!source) return snap25(value);
  const candidates = mode === 'bar'
    ? source.beats.filter(beat => beat.isDownbeat)
    : source.beats;
  if (!candidates.length) return snap25(value);
  const nearest = candidates.reduce((best, beat) =>
    Math.abs(beat.timeMs - value) < Math.abs(best.timeMs - value) ? beat : best
  );
  return snap25(nearest.timeMs);
}

async function importProjectFile(event) {
  const file = event.target.files?.[0];
  event.target.value = '';
  if (!file) return;
  setBusy(true, 'Importing project');
  try {
    const project = normalizeImportedProject(JSON.parse(await file.text()));
    await applyImportedProject(project, file.name);
    scheduleProjectSave(0);
    setStatus('Project imported');
    showToast(`Imported ${file.name}; future changes will auto-save in the workspace.`);
  } catch (error) {
    showToast(error.message, true);
    setStatus('Import failed');
  } finally {
    setBusy(false);
  }
}

function exportProjectCopy() {
  const project = createStudioProject();
  const blob = new Blob([JSON.stringify(project, null, 2)], {
    type: 'application/vnd.moving-head-studio.project+json',
  });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = `${slug(state.document.sequenceName)}-${state.document.headCount}mh.mhproj`;
  link.click();
  URL.revokeObjectURL(link.href);
  showToast(`Exported project copy ${link.download}`);
}

function createStudioProject() {
  return {
    format: projectFormat,
    version: projectVersion,
    revision: state.projectRevision ?? crypto.randomUUID().replaceAll('-', ''),
    savedAtUtc: new Date().toISOString(),
    source: {
      sequenceId: state.document.sequenceId,
      fileName: state.document.sourceFileName,
      name: state.document.sequenceName,
      fingerprint: state.document.sourceFingerprint,
      durationMs: state.document.cueSheet.durationMs,
    },
    editor: {
      headCount: state.document.headCount,
      timingSourceName: state.timingSourceName,
      outputFileName: elements.outputFileName.value,
      fixtureProfile: state.document.fixtureProfile,
      cueSheet: state.document.cueSheet,
    },
    preview: {
      mountMode: state.mountMode,
      fixtureRotations: [...state.fixtureRotations],
    },
    view: {
      selectedCueIndex: state.selectedIndex,
      currentMs: Math.round(state.currentMs),
    },
    regeneration: {
      summary: state.regenerationSummary,
      appliedChoices: state.appliedRegenerationChoices,
    },
  };
}

function normalizeImportedProject(project) {
  if (project?.format !== projectFormat || project.version !== projectVersion) {
    throw new Error(
      `Unsupported project format/version; expected ${projectFormat} v${projectVersion}.`
    );
  }
  if (!project.source?.fileName ||
      !Array.isArray(project.editor?.cueSheet?.sections) ||
      !Number.isFinite(project.editor.cueSheet.durationMs)) {
    throw new Error('The selected project is incomplete.');
  }
  return project;
}

async function applyImportedProject(project, fileName) {
  const target = state.document.sequences.find(sequence =>
    sequence.fileName.toLowerCase() === project.source.fileName.toLowerCase()
  ) ?? state.document.sequences.find(sequence => sequence.id === project.source.sequenceId);
  if (!target) {
    throw new Error(`Source sequence "${project.source.fileName}" is not available in this workspace.`);
  }
  if (project.source.durationMs !== project.editor.cueSheet.durationMs) {
    throw new Error('The project source and cue-sheet durations do not match.');
  }
  const linkedProject = await api(
    `/api/project?sequence=${encodeURIComponent(target.id)}&headCount=${project.editor.headCount}`
  );
  const document = await api('/api/preview', {
    method: 'POST',
    body: JSON.stringify({
      sequenceId: target.id,
      headCount: project.editor.headCount,
      cueSheet: project.editor.cueSheet,
      fixtureProfile: project.editor.fixtureProfile,
      timingSourceName: project.editor.timingSourceName,
      outputFileName: project.editor.outputFileName,
      force: false,
    }),
  });
  if (project.source.fingerprint && document.sourceFingerprint !== project.source.fingerprint &&
      !window.confirm(
        `"${project.source.fileName}" changed since this project copy was exported. ` +
        'Import it against the current sequence anyway?'
      )) {
    throw new Error('Project import cancelled.');
  }
  commitLoadedProject(document, project, linkedProject.project?.revision ?? null);
}

async function resetHiddenProject() {
  if (!state.document || !window.confirm(
    `Rebuild ${state.document.sourceFileName} from the sequence? ` +
    'The hidden editor project will be archived.'
  )) return;
  setBusy(true, 'Rebuilding from sequence');
  try {
    await api('/api/project/reset', {
      method: 'POST',
      body: JSON.stringify({
        sequenceId: state.document.sequenceId,
        headCount: state.document.headCount,
      }),
    });
    await loadBootstrap(state.document.sequenceId, state.document.headCount, { ignoreProject: true });
    showToast('Rebuilt editor project from the sequence.');
  } catch (error) {
    showToast(error.message, true);
    setStatus('Rebuild failed');
  } finally {
    setBusy(false);
  }
}

function commitLoadedProject(document, project, linkedRevision) {
  if (state.document?.sequenceId !== document.sequenceId) clearLinkedAudio();
  state.document = document;
  state.projectRevision = linkedRevision;
  state.timingSourceName = document.timing.beatSources.some(source =>
    source.trackName === project.editor.timingSourceName
  ) ? project.editor.timingSourceName : document.timing.defaultBeatSource;
  elements.outputFileName.value = project.editor.outputFileName || document.suggestedOutputFileName;
  state.mountMode = project.preview?.mountMode === 'truss' ? 'truss' : 'floor';
  applyMountMode();
  state.fixtureRotations = normalizeProjectRotations(
    project.preview?.fixtureRotations,
    document.headCount
  );
  saveFixtureRotations();
  state.previewHeads = [];
  state.selectedIndex = Math.max(0, Math.min(
    project.view?.selectedCueIndex ?? 0,
    document.cueSheet.sections.length - 1
  ));
  state.currentMs = Math.max(0, Math.min(
    project.view?.currentMs ?? document.cueSheet.sections[state.selectedIndex].startMs,
    document.preview.durationMs
  ));
  state.playing = false;
  state.suggestions = null;
  state.regenerationUndo = null;
  state.regenerationSummary = project.regeneration?.summary ?? null;
  state.appliedRegenerationChoices = project.regeneration?.appliedChoices ?? null;
  state.backupCatalog = null;
  setLoopSelected(false);
  renderDocument({ rebuildSelectors: true });
  loadSuggestions();
}

function normalizeProjectRotations(values, headCount) {
  if (!Array.isArray(values) || values.length !== headCount) {
    return loadFixtureRotations(state.document.sequenceId, headCount);
  }
  return values.map(normalizeRotation);
}

function scheduleProjectSave(delay = 500) {
  if (!state.document) return;
  clearTimeout(state.projectSaveTimer);
  elements.workspaceStatus.textContent = 'Saving project…';
  const revision = ++state.projectSaveRevision;
  state.projectSaveTimer = setTimeout(() => saveHiddenProject(revision), delay);
}

async function saveHiddenProject(revision) {
  if (!state.document || revision !== state.projectSaveRevision) return;
  try {
    const result = await api('/api/project', {
      method: 'POST',
      body: JSON.stringify({
        editor: createRequest(),
        expectedSourceFingerprint: state.document.sourceFingerprint,
        expectedProjectRevision: state.projectRevision,
        preview: {
          mountMode: state.mountMode,
          fixtureRotations: state.fixtureRotations,
        },
        view: {
          selectedCueIndex: state.selectedIndex,
          currentMs: Math.round(state.currentMs),
        },
        regeneration: {
          summary: state.regenerationSummary,
          appliedChoices: state.appliedRegenerationChoices,
        },
      }),
    });
    if (revision !== state.projectSaveRevision) return;
    elements.workspaceStatus.textContent = 'Project saved';
    state.document.sourceFingerprint = result.sourceFingerprint;
    state.projectRevision = result.projectRevision;
  } catch (error) {
    if (revision !== state.projectSaveRevision) return;
    elements.workspaceStatus.textContent = 'Project not saved';
    showToast(error.message, true);
  }
}

function openGenerateDialog() {
  if (!state.document) return;
  elements.generateSequenceSummary.textContent =
    `${state.document.sequenceName} · ${state.document.headCount} heads · ` +
    `${state.document.cueSheet.sections.length} cues`;
  elements.generationModeControl.querySelector('input[value="new"]').checked = true;
  renderGenerationMode();
  elements.generateDialog.showModal();
  elements.outputFileName.focus();
  elements.outputFileName.select();
  void loadBackups();
}

function closeGenerateDialog() {
  if (elements.generateDialog.open) elements.generateDialog.close();
}

function renderGenerationMode() {
  const original = generationMode() === 'original';
  elements.newFilePanel.hidden = original;
  elements.originalFilePanel.hidden = !original;
  elements.confirmGenerateButton.textContent = original
    ? 'Back up & replace original'
    : 'Generate XSQ';
  const source = selectedSequenceOption();
  elements.originalSourceFileName.textContent = source?.fileName ?? 'Original sequence';
}

function generationMode() {
  return elements.generationModeControl.querySelector('input:checked')?.value ?? 'new';
}

function selectedSequenceOption() {
  return state.document?.sequences.find(sequence => sequence.id === state.document.sequenceId);
}

async function loadBackups() {
  if (!state.document) return;
  elements.refreshBackupsButton.disabled = true;
  elements.backupList.textContent = 'Loading backups';
  try {
    state.backupCatalog = await api(
      `/api/backups?sequence=${encodeURIComponent(state.document.sequenceId)}`
    );
    renderBackups();
  } catch (error) {
    elements.backupList.textContent = 'Backups unavailable';
    showToast(error.message, true);
  } finally {
    elements.refreshBackupsButton.disabled = false;
  }
}

function renderBackups() {
  const backups = state.backupCatalog?.backups ?? [];
  if (!backups.length) {
    elements.backupList.replaceChildren(createBackupEmptyState());
    return;
  }
  elements.backupList.replaceChildren(...backups.map(backup => {
    const row = document.createElement('div');
    row.className = 'backup-row';
    const details = document.createElement('div');
    const timestamp = document.createElement('strong');
    timestamp.textContent = new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'medium',
    }).format(new Date(backup.createdAtUtc));
    const size = document.createElement('span');
    size.textContent = formatFileSize(backup.sizeBytes);
    details.append(timestamp, size);
    const restore = document.createElement('button');
    restore.type = 'button';
    restore.className = 'quiet-button backup-restore-button';
    restore.dataset.backupId = backup.id;
    restore.title = backup.id;
    restore.textContent = 'Restore';
    row.append(details, restore);
    return row;
  }));
}

function createBackupEmptyState() {
  const empty = document.createElement('p');
  empty.className = 'backup-empty';
  empty.textContent = 'No backups yet';
  return empty;
}

function formatFileSize(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

async function generateSequence() {
  if (generationMode() === 'original') {
    await generateOriginalSequence();
    return;
  }
  setBusy(true, 'Generating XSQ');
  try {
    let request = createRequest();
    request.outputFileName = elements.outputFileName.value;
    request.force = false;
    let response = await fetch('/api/generate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    let payload = await response.json();
    if (!response.ok && /already exists/i.test(payload.detail ?? payload.error ?? '') &&
        window.confirm('That output already exists. Replace it?')) {
      request.force = true;
      response = await fetch('/api/generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
      });
      payload = await response.json();
    }
    if (!response.ok) throw new Error(payload.detail ?? payload.error ?? 'Generation failed.');
    closeGenerateDialog();
    showToast(`Created and validated ${payload.outputPath}`);
    setStatus('XSQ generated · validated');
  } catch (error) {
    showToast(error.message, true);
    setStatus('Generation failed');
  } finally {
    setBusy(false);
  }
}

async function generateOriginalSequence() {
  const source = selectedSequenceOption();
  if (!source || !window.confirm(
    `Replace "${source.fileName}"? A verified backup will be created first.`
  )) return;

  setBusy(true, 'Backing up and replacing original');
  try {
    const result = await api('/api/generate-original', {
      method: 'POST',
      body: JSON.stringify(createRequest()),
    });
    closeGenerateDialog();
    state.backupCatalog = null;
    state.projectRevision = null;
    state.document.sourceFingerprint = result.sourceFingerprint;
    scheduleProjectSave(0);
    elements.outputName.textContent = source.fileName;
    setStatus('Original updated · validated');
    showToast(`Original validated and updated · backup ${formatBackupTime(result.backup.createdAtUtc)}`);
  } catch (error) {
    showToast(error.message, true);
    setStatus('Original unchanged');
  } finally {
    setBusy(false);
  }
}

async function restoreBackup(backupId) {
  const source = selectedSequenceOption();
  const backup = state.backupCatalog?.backups.find(candidate => candidate.id === backupId);
  if (!source || !backup || !window.confirm(
    `Restore the ${formatBackupTime(backup.createdAtUtc)} backup of "${source.fileName}"? ` +
    'The current original will be backed up first and the editor will reload.'
  )) return;

  setBusy(true, 'Restoring original backup');
  try {
    await api('/api/restore-backup', {
      method: 'POST',
      body: JSON.stringify({ sequenceId: state.document.sequenceId, backupId }),
    });
    const sequenceId = state.document.sequenceId;
    const headCount = state.document.headCount;
    closeGenerateDialog();
    await loadBootstrap(sequenceId, headCount);
    showToast(`Restored backup from ${formatBackupTime(backup.createdAtUtc)}`);
  } catch (error) {
    showToast(error.message, true);
    setStatus('Restore failed');
  } finally {
    setBusy(false);
  }
}

function formatBackupTime(value) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

function createRequest() {
  return {
    sequenceId: state.document.sequenceId,
    headCount: state.document.headCount,
    cueSheet: state.document.cueSheet,
    fixtureProfile: state.document.fixtureProfile,
    timingSourceName: state.timingSourceName,
    outputFileName: elements.outputFileName.value,
    force: false,
    expectedSourceFingerprint: state.document.sourceFingerprint,
  };
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(options.headers ?? {}) },
    ...options,
  });
  const payload = await response.json();
  if (!response.ok) throw new Error(payload.detail ?? payload.error ?? `Request failed (${response.status}).`);
  return payload;
}

function setBusy(busy, message = '') {
  const shell = document.querySelector('.app-shell');
  shell.inert = busy;
  shell.setAttribute('aria-busy', String(busy));
  elements.confirmGenerateButton.disabled = busy;
  elements.cancelGenerateButton.disabled = busy;
  elements.generateDialogCloseButton.disabled = busy;
  elements.generateDialog.querySelectorAll('input, button').forEach(control => {
    control.disabled = busy;
  });
  elements.workspaceDialog.querySelectorAll('input, button').forEach(control => {
    control.disabled = busy;
  });
  if (message) setStatus(message);
}

function setStatus(message) {
  elements.saveStatus.textContent = message;
}

function showToast(message, error = false) {
  clearTimeout(state.toastTimer);
  elements.toast.textContent = message;
  elements.toast.className = `toast show${error ? ' error' : ''}`;
  state.toastTimer = setTimeout(() => { elements.toast.className = 'toast'; }, 4200);
}

function formatTime(milliseconds) {
  const value = Math.max(0, Math.round(milliseconds));
  const minutes = Math.floor(value / 60000);
  const seconds = Math.floor((value % 60000) / 1000);
  const millis = value % 1000;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
}

function splitWords(value) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function formatDimmer(value) {
  const all = /^All(\d+)$/.exec(value);
  if (all) return `All · ${all[1]}%`;
  if (value === 'Fade75') return 'Fade · 75% → 30%';
  if (value === 'Fade30') return 'Fade · 30% → 0%';
  return splitWords(value);
}

function range(start, end) {
  return Array.from({ length: Math.max(0, end - start + 1) }, (_, index) => start + index);
}

function snap25(value) {
  return Math.round(value / 25) * 25;
}

function lerp(start, end, progress) {
  return start + ((end - start) * progress);
}

function truncateCanvasText(context, value, maximumWidth) {
  if (context.measureText(value).width <= maximumWidth) return value;
  let text = value;
  while (text.length > 1 && context.measureText(`${text}…`).width > maximumWidth) {
    text = text.slice(0, -1);
  }
  return `${text}…`;
}

function sliceCurveKey(key, sourceStart, sourceEnd, sliceStart, sliceEnd) {
  if (!key || (sliceStart === sourceStart && sliceEnd === sourceEnd)) return key;
  const values = [...key.matchAll(/\d+(?:\.\d+)?/g)].map(match => Number(match[0]));
  if (!values.length) return key;
  const prefix = key[0];
  if (values.length === 1) return `${prefix}${Math.round(values[0])}`;
  const start = evaluateCurveValues(values, sourceStart, sourceEnd, sliceStart);
  const end = evaluateCurveValues(values, sourceStart, sourceEnd, sliceEnd);
  if (values.length === 3) {
    const midpoint = sourceStart + ((sourceEnd - sourceStart) / 2);
    if (sliceStart < midpoint && sliceEnd > midpoint) {
      return `${prefix}B${Math.round(start)}_${Math.round(values[1])}_${Math.round(end)}`;
    }
  }
  const roundedStart = Math.round(start);
  const roundedEnd = Math.round(end);
  return roundedStart === roundedEnd
    ? `${prefix}${roundedStart}`
    : `${prefix}${roundedStart}_${roundedEnd}`;
}

function evaluateCurveValues(values, startMs, endMs, timeMs) {
  const progress = endMs <= startMs ? 0 : Math.max(0, Math.min(1, (timeMs - startMs) / (endMs - startMs)));
  if (values.length === 2) return lerp(values[0], values[1], progress);
  if (values.length === 3) {
    return progress <= .5
      ? lerp(values[0], values[1], progress * 2)
      : lerp(values[1], values[2], (progress - .5) * 2);
  }
  return values[0];
}

function slug(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}
