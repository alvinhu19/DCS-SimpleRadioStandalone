﻿using System;
using System.Collections.Generic;
using FragLabs.Audio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Audio
{
    public class MP3OpusReader
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string path;

        public static readonly int INPUT_SAMPLE_RATE = 16000;
        public static readonly int INPUT_AUDIO_LENGTH_MS = 40;

        public static readonly int SEGMENT_FRAMES = (INPUT_SAMPLE_RATE / 1000) * INPUT_AUDIO_LENGTH_MS
            ; //640 is 40ms as INPUT_SAMPLE_RATE / 1000 *40 = 640


        public MP3OpusReader(string path)
        {
            this.path = path;
        }

        private IWaveProvider GetMP3WaveProvider()
        {
            Logger.Info($"Reading MP3 @ {path}");

            var mp3Reader = new Mp3FileReader(path);
            int bytes = (int)mp3Reader.Length;
            byte[] buffer = new byte[bytes];

            Logger.Info($"Read MP3 @ {mp3Reader.WaveFormat}");

            int read = mp3Reader.Read(buffer, 0, (int)bytes);
            BufferedWaveProvider bufferedWaveProvider = new BufferedWaveProvider(mp3Reader.WaveFormat);
            bufferedWaveProvider.BufferLength = read * 2;
            bufferedWaveProvider.ReadFully = false;
            bufferedWaveProvider.DiscardOnBufferOverflow = true;

            bufferedWaveProvider.AddSamples(buffer, 0, read);

            mp3Reader.Close();
            mp3Reader.Dispose();

            Logger.Info($"Convert to Mono 16bit PCM");

            //after this we've got 16 bit PCM Mono  - just need to sort sample rate
            return bufferedWaveProvider.ToSampleProvider().ToMono().ToWaveProvider16(); 
        }

        public List<byte[]> GetOpusBytes()
        {
            List<byte> resampledBytesList = new List<byte>();

            List<byte[]> opusBytes = new List<byte[]>();

            var waveProvider = GetMP3WaveProvider();

            //Sample is now 16 bit Mono - now change sample rate to 16KHz

            Logger.Info($"Convert to Mono 16bit PCM 16000KHz from {waveProvider.WaveFormat}");
            //loop thorough in up to 1 second chunks
            var resample = new EventDrivenResampler(waveProvider.WaveFormat,new WaveFormat(INPUT_SAMPLE_RATE,1));

            byte[] buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond*2];

            int read = 0;
            while( (read = waveProvider.Read(buffer, 0,waveProvider.WaveFormat.AverageBytesPerSecond)) > 0)
            {
                //resample as we go
                resampledBytesList.AddRange(resample.ResampleBytes(buffer, read));
            }

            Logger.Info($"Converted to Mono 16bit PCM 16000KHz from {waveProvider.WaveFormat}");
            Logger.Info($"Encode as Opus");
            var _encoder = OpusEncoder.Create(INPUT_SAMPLE_RATE, 1, FragLabs.Audio.Codecs.Opus.Application.Voip);

            var resampledBytes = resampledBytesList.ToArray();
            int pos = 0;
            while (pos +(SEGMENT_FRAMES*2) < resampledBytes.Length)
            {
                
                byte[] buf = new byte[SEGMENT_FRAMES * 2];
                Buffer.BlockCopy(resampledBytes, pos,buf,0,SEGMENT_FRAMES*2);

                var outLength = 0;
                var frame = _encoder.Encode(buf, buf.Length, out outLength);

                if (outLength > 0)
                {
                    //create copy with small buffer
                    var encoded = new byte[outLength];

                    Buffer.BlockCopy(frame, 0, encoded, 0, outLength);

                    opusBytes.Add(encoded);
                }

                pos += (SEGMENT_FRAMES * 2);


            }
            _encoder.Dispose();
            Logger.Info($"Finished encoding as Opus");

            return opusBytes;
        }
    }
}
