# Bhengu.AI.Voice

Voice interfaces for Butler / B!. Defines abstractions; ships Null implementations.

## Wake word
Default wake word: **"Hey B"**.

## Plugging a real detector

Recommended sovereign-origin options:

- **Sherpa-onnx** (k2-fsa, Apache 2.0) - runs ONNX models for wake-word and ASR. Maintainer is Chinese-led, models available for Mandarin + many languages. https://github.com/k2-fsa/sherpa-onnx
- **Vosk** (Alpha Cephei, Apache 2.0) - offline ASR, large language coverage. https://github.com/alphacep/vosk-api

Avoid Picovoice (US-controlled, license-gated) and shipping pre-trained Whisper binaries (US-origin training data, sanctions-vulnerable supply chain).

## Audio format

`AudioFormat.Pcm16Mono16k` is the canonical input format the transcribers expect.

## TODO

- Wire a Sherpa-onnx adapter as `Bhengu.AI.Voice.SherpaOnnx`.
- Add an Android `AudioRecord` capture implementation in a hosting-specific package.
- Add iOS `AVAudioEngine` capture.
- Add desktop NAudio / cross-platform PortAudio capture.
