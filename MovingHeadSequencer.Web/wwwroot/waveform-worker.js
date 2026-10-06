self.onmessage = event => {
  const { channels, bins } = event.data;
  if (!channels?.length || !channels[0]?.length || bins <= 0) {
    self.postMessage({ peaks: [] });
    return;
  }

  const length = channels[0].length;
  const peaks = new Array(bins);
  for (let bin = 0; bin < bins; bin++) {
    const start = Math.floor((bin * length) / bins);
    const end = Math.max(start + 1, Math.floor(((bin + 1) * length) / bins));
    let minimum = 1;
    let maximum = -1;
    let squareSum = 0;
    let sampleCount = 0;
    for (let index = start; index < end && index < length; index++) {
      let sample = 0;
      for (const channel of channels) sample += channel[index] ?? 0;
      sample /= channels.length;
      minimum = Math.min(minimum, sample);
      maximum = Math.max(maximum, sample);
      squareSum += sample * sample;
      sampleCount++;
    }
    peaks[bin] = {
      min: minimum,
      max: maximum,
      rms: sampleCount ? Math.sqrt(squareSum / sampleCount) : 0,
    };
  }
  self.postMessage({ peaks });
};
