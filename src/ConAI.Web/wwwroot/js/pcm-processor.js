class PcmProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.buffer = new Int16Array(512);
        this.filled = 0;
        this.ratio = sampleRate / 16000;
        this.position = 0;
    }

    process(inputs) {
        const channel = inputs[0]?.[0];
        if (!channel) {
            return true;
        }

        while (this.position < channel.length) {
            const sample = Math.max(-1, Math.min(1, channel[Math.floor(this.position)]));
            this.buffer[this.filled] = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
            this.filled += 1;
            this.position += this.ratio;

            if (this.filled === this.buffer.length) {
                const chunk = this.buffer.slice();
                this.port.postMessage(chunk.buffer, [chunk.buffer]);
                this.filled = 0;
            }
        }

        this.position -= channel.length;
        return true;
    }
}

registerProcessor('pcm-processor', PcmProcessor);
