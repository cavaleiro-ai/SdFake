package com.sdfake.app.engine;

import java.util.ArrayList;
import java.util.List;

public final class FullResult {
    public static final int GENUINE = 0, FAKE = 1, DEFECTIVE = 2, INCOMPLETE = 3, ERROR = 4, UNCONFIRMED = 5;

    public int verdict = ERROR;
    public long totalBytes, freeBefore, targetBytes, writtenBytes, okBytes, badBytes;
    public long firstBadOffset = -1;
    public int badZero, badAlias, badOther, badUnreadable;   // em blocos de 1 MiB
    public boolean stoppedEarly, cancelled, verified;
    public String writeError, fatalError;
    public double writeMBs, readMBs;
    /** Velocidade so das leituras (sem contar a conferencia), para detectar leitura vinda da memoria. */
    public double readIoMBs;
    public long estimatedRealBytes = -1;
    public long elapsedMs;

    /**
     * O Android nao informa ao app o tamanho que o dispositivo fisico anuncia, so o do volume
     * formatado. Um volume minusculo que passa no teste costuma ser um cartao falso ja reformatado
     * no tamanho real, entao esse caso e sinalizado em vez de aparecer como aprovado.
     */
    public boolean suspiciouslySmall() {
        return verdict == GENUINE && totalBytes > 0 && totalBytes < 1_500_000_000L;
    }

    public String title() {
        switch (verdict) {
            case GENUINE: return Fmt.bytes(totalBytes) + " REAIS";
            case FAKE: return "FALSO";
            case DEFECTIVE: return "COM DEFEITO";
            case INCOMPLETE: return "TESTE INCOMPLETO";
            case UNCONFIRMED: return "NÃO CONFIRMADO";
            default: return "ERRO";
        }
    }

    public String summary() {
        switch (verdict) {
            case GENUINE:
                return "O cartão está formatado com " + Fmt.bytes(totalBytes) + " e todo o espaço livre (" + Fmt.bytes(writtenBytes)
                        + ") foi gravado e conferido sem nenhum erro. "
                        + (suspiciouslySmall()
                        ? "ATENÇÃO: esse tamanho é pequeno demais para um cartão atual. Se ele foi vendido como maior, é um cartão falso que alguém reformatou no tamanho real."
                        : "Compare com o que está escrito no cartão: se a embalagem promete bem mais do que isso, ele é falso.");
            case FAKE:
                if (badBytes == 0 && writeError != null)
                    return "O dispositivo passou a recusar gravações depois de " + Fmt.bytes(writtenBytes)
                            + ". Capacidade utilizável aproximada: " + Fmt.bytes(estimatedRealBytes)
                            + " (anunciado: " + Fmt.bytes(totalBytes) + ").";
                return "Capacidade real aproximada: " + Fmt.bytes(estimatedRealBytes) + "  —  anunciado: " + Fmt.bytes(totalBytes) + ".";
            case DEFECTIVE:
                return "A capacidade parece verdadeira, mas " + Fmt.bytes(badBytes)
                        + " de dados voltaram corrompidos. Não guarde arquivos importantes neste dispositivo.";
            case INCOMPLETE:
                return badBytes > 0
                        ? "Teste cancelado, mas já havia " + Fmt.bytes(badBytes) + " de dados corrompidos: forte sinal de dispositivo falso ou com defeito."
                        : "Teste cancelado antes do fim; não dá para concluir nada. Rode de novo até terminar.";
            case UNCONFIRMED:
                return "Os " + Fmt.bytes(writtenBytes) + " voltaram corretos, mas não deu para garantir que foram lidos do cartão "
                        + "e não da memória do celular. Retire o cartão, coloque de novo e repita o teste, ou teste no computador.";
            default:
                return fatalError != null ? fatalError : "Erro inesperado.";
        }
    }

    public List<String> lines() {
        List<String> l = new ArrayList<>();
        if (totalBytes > 0) l.add("Tamanho com que o cartão está formatado: " + Fmt.bytes(totalBytes));
        if (targetBytes > 0) l.add("Espaço livre testado: " + Fmt.bytes(writtenBytes) + " de " + Fmt.bytes(targetBytes));
        if (verified || badBytes > 0) {
            l.add("Dados conferidos e corretos: " + Fmt.bytes(okBytes));
            l.add("Dados perdidos ou corrompidos: " + Fmt.bytes(badBytes));
        }
        if (firstBadOffset >= 0) l.add("Primeiro erro apareceu depois de " + Fmt.bytes(firstBadOffset) + " de dados de teste");
        if (badZero > 0) l.add("Blocos que voltaram vazios (nunca foram gravados de verdade): " + badZero);
        if (badAlias > 0) l.add("Blocos sobrescritos por outros dados (o cartão regrava por cima do começo): " + badAlias);
        if (badOther > 0) l.add("Blocos com dados corrompidos: " + badOther);
        if (badUnreadable > 0) l.add("Blocos ilegíveis (erro de leitura): " + badUnreadable);
        if (writeError != null) l.add("Erro de gravação: " + writeError);
        if (estimatedRealBytes >= 0 && verdict == FAKE)
            l.add("A capacidade real é uma estimativa: espaço já ocupado antes do teste + dados que sobreviveram.");
        if (writeMBs > 0) l.add("Velocidade média de gravação: " + Fmt.speed(writeMBs));
        if (readMBs > 0) l.add("Velocidade média de leitura: " + Fmt.speed(readMBs));
        l.add("Duração: " + Fmt.duration(elapsedMs));
        return l;
    }
}
