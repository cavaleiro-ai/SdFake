package com.sdfake.app;

import android.content.ContentResolver;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import android.system.Os;
import android.system.StructStatVfs;

import com.sdfake.app.engine.Store;
import com.sdfake.app.engine.StoreException;
import com.sdfake.app.engine.TestFile;

import java.io.FileDescriptor;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;

/**
 * Pasta de teste dentro do cartao/pendrive, acessada pelo seletor de pastas do Android.
 *
 * O arquivo aberto assim aponta direto para o sistema de arquivos do cartao. Depois de cada
 * gravacao o app forca a ida dos dados para o cartao (fsync) e descarta a copia que o Android
 * guarda na memoria (posix_fadvise DONTNEED), para que a conferencia leia do cartao de verdade.
 */
final class SafStore implements Store {
    static final String FOLDER = "SdFake_Teste";
    static final String AUTHORITY = "com.android.externalstorage.documents";
    private static final String MIME = "application/octet-stream";
    private static final Pattern OURS = Pattern.compile("SDF(\\d{5}|probe)\\.bin");
    private static final long SYNC_EVERY = 32L << 20;

    private final ContentResolver resolver;
    private final Uri tree;
    private final Map<String, Uri> files = new HashMap<>();
    private Uri dir;
    private long total, free;
    private volatile boolean cacheDropFailed;

    SafStore(ContentResolver resolver, Uri tree) {
        this.resolver = resolver;
        this.tree = tree;
    }

    @Override
    public void prepare() throws IOException {
        try {
            String rootId = DocumentsContract.getTreeDocumentId(tree);
            Uri rootDoc = DocumentsContract.buildDocumentUriUsingTree(tree, rootId);
            removeLeftovers(rootId);
            dir = DocumentsContract.createDocument(resolver, rootDoc, Document.MIME_TYPE_DIR, FOLDER);
            if (dir == null) throw new StoreException("Não foi possível criar a pasta de teste no dispositivo.");

            Uri probe = DocumentsContract.createDocument(resolver, dir, MIME, "SDFprobe.bin");
            if (probe == null) throw new StoreException("Não foi possível gravar no dispositivo.");
            try (ParcelFileDescriptor pfd = resolver.openFileDescriptor(probe, "rw")) {
                if (pfd == null) throw new StoreException("Não foi possível abrir o dispositivo.");
                StructStatVfs st = Os.fstatvfs(pfd.getFileDescriptor());
                total = st.f_blocks * st.f_frsize;
                free = st.f_bavail * st.f_frsize;
            } finally {
                delete(probe);
            }
        } catch (StoreException e) {
            throw e;
        } catch (Exception e) {
            throw StoreException.wrap("Não foi possível preparar o dispositivo", e);
        }
    }

    /** false se em algum momento nao foi possivel descartar a copia em memoria dos arquivos. */
    @Override
    public boolean readsBypassCache() {
        return !cacheDropFailed;
    }

    @Override
    public long freeBytes() {
        return free;
    }

    @Override
    public long totalBytes() {
        return total;
    }

    @Override
    public TestFile create(String name) throws IOException {
        try {
            Uri uri = DocumentsContract.createDocument(resolver, dir, MIME, name);
            if (uri == null) throw new StoreException("Não foi possível criar o arquivo de teste.");
            files.put(name, uri);
            ParcelFileDescriptor pfd = resolver.openFileDescriptor(uri, "w");
            if (pfd == null) throw new StoreException("Não foi possível abrir o arquivo de teste.");
            return new FdFile(this, pfd, true);
        } catch (StoreException e) {
            throw e;
        } catch (Exception e) {
            throw StoreException.wrap("Não foi possível criar o arquivo de teste", e);
        }
    }

    @Override
    public TestFile open(String name) throws IOException {
        try {
            Uri uri = files.get(name);
            if (uri == null) throw new StoreException("Arquivo de teste desconhecido: " + name);
            ParcelFileDescriptor pfd = resolver.openFileDescriptor(uri, "r");
            if (pfd == null) throw new StoreException("Não foi possível abrir o arquivo de teste.");
            return new FdFile(this, pfd, false);
        } catch (StoreException e) {
            throw e;
        } catch (Exception e) {
            throw StoreException.wrap("Não foi possível abrir o arquivo de teste", e);
        }
    }

    @Override
    public void cleanup() {
        try {
            for (Uri uri : files.values()) delete(uri);
            files.clear();
            if (dir != null) {
                String dirId = DocumentsContract.getDocumentId(dir);
                removeOurs(dirId);
                if (children(dirId).isEmpty()) delete(dir);
                dir = null;
            }
        } catch (Exception ignored) {
            // sem acesso ao dispositivo: nada mais a fazer
        }
    }

    // ------------------------------------------------------------------ auxiliares

    private void delete(Uri uri) {
        try {
            DocumentsContract.deleteDocument(resolver, uri);
        } catch (Exception ignored) {
            // ja apagado ou dispositivo removido
        }
    }

    /** {id, nome, tipo} de cada item da pasta. */
    private List<String[]> children(String parentId) {
        List<String[]> out = new ArrayList<>();
        Uri uri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId);
        String[] cols = { Document.COLUMN_DOCUMENT_ID, Document.COLUMN_DISPLAY_NAME, Document.COLUMN_MIME_TYPE };
        try (Cursor c = resolver.query(uri, cols, null, null, null)) {
            while (c != null && c.moveToNext()) out.add(new String[] { c.getString(0), c.getString(1), c.getString(2) });
        }
        return out;
    }

    /** Apaga somente os arquivos SDFnnnnn.bin criados por este app. */
    private void removeOurs(String dirId) {
        for (String[] child : children(dirId)) {
            if (child[1] != null && OURS.matcher(child[1]).matches())
                delete(DocumentsContract.buildDocumentUriUsingTree(tree, child[0]));
        }
    }

    /** Restos de um teste interrompido: limpa a pasta SdFake_Teste e a remove se ficar vazia. */
    private void removeLeftovers(String rootId) {
        for (String[] child : children(rootId)) {
            if (!FOLDER.equals(child[1]) || !Document.MIME_TYPE_DIR.equals(child[2])) continue;
            removeOurs(child[0]);
            if (children(child[0]).isEmpty()) delete(DocumentsContract.buildDocumentUriUsingTree(tree, child[0]));
        }
    }

    /** Arquivo de teste sobre um descritor entregue pelo provedor de armazenamento. */
    private static final class FdFile implements TestFile {
        private final SafStore owner;
        private final ParcelFileDescriptor pfd;
        private final FileDescriptor fd;
        private final FileChannel channel;
        private final boolean writing;
        private long sinceSync;

        FdFile(SafStore owner, ParcelFileDescriptor pfd, boolean writing) {
            this.owner = owner;
            this.pfd = pfd;
            this.fd = pfd.getFileDescriptor();
            this.writing = writing;
            this.channel = writing ? new FileOutputStream(fd).getChannel() : new FileInputStream(fd).getChannel();
            if (!writing) dropCache();
        }

        /** Pede ao Android para esquecer a copia em memoria deste arquivo. */
        private void dropCache() {
            if (!NativeIo.drop(pfd.getFd())) owner.cacheDropFailed = true;
        }

        @Override
        public void write(ByteBuffer buffer) throws IOException {
            int n = buffer.remaining();
            while (buffer.hasRemaining()) channel.write(buffer);
            sinceSync += n;
            if (sinceSync >= SYNC_EVERY) flush();
        }

        @Override
        public void read(long offset, ByteBuffer buffer) throws IOException {
            long pos = offset;
            while (buffer.hasRemaining()) {
                int n = channel.read(buffer, pos);
                if (n < 0) throw new StoreException("O arquivo de teste está menor do que deveria.");
                pos += n;
            }
        }

        @Override
        public void flush() throws IOException {
            fd.sync();
            sinceSync = 0;
            dropCache();
        }

        @Override
        public void close() throws IOException {
            if (!writing) dropCache();
            try {
                channel.close();
            } finally {
                pfd.close();
            }
        }
    }
}
