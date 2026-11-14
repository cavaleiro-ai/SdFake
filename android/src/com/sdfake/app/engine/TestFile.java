package com.sdfake.app.engine;

import java.io.Closeable;
import java.io.IOException;
import java.nio.ByteBuffer;

/** Um arquivo de teste: gravacao sequencial, leitura por posicao. */
public interface TestFile extends Closeable {
    /** Grava todo o conteudo do buffer (da posicao ate o limite) no fim do arquivo. */
    void write(ByteBuffer buffer) throws IOException;

    /** Enche o buffer inteiro a partir de offset. Deve ler do dispositivo, nao de cache. */
    void read(long offset, ByteBuffer buffer) throws IOException;

    /** Garante que tudo foi para o dispositivo. */
    void flush() throws IOException;
}
