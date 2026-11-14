package com.sdfake.app.engine;

import java.io.IOException;

/** Onde os arquivos de teste sao gravados (uma pasta dentro do cartao). */
public interface Store {
    /** Cria a pasta de teste, removendo restos de um teste anterior. */
    void prepare() throws IOException;

    long freeBytes() throws IOException;

    long totalBytes() throws IOException;

    TestFile create(String name) throws IOException;

    TestFile open(String name) throws IOException;

    /**
     * true se as leituras de conferencia vem mesmo do dispositivo (e nao de uma copia em memoria
     * mantida pelo sistema). Quando false, um resultado "verdadeiro" em teste pequeno nao e confiavel.
     */
    default boolean readsBypassCache() {
        return true;
    }

    /** Apaga os arquivos de teste. Nunca lanca excecao. */
    void cleanup();
}
