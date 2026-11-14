// Biblioteca nativa minima do SdFake.
//
// O Android guarda na memoria RAM uma copia do que acabou de ser gravado. Se a conferencia
// lesse essa copia, um cartao falso pareceria perfeito. posix_fadvise(DONTNEED), chamado depois
// do fsync, manda o sistema descartar essa copia, forcando a leitura a vir do cartao.
// O Java do Android nao oferece essa chamada, por isso este arquivo existe.
#include <errno.h>
#include <fcntl.h>
#include <jni.h>

JNIEXPORT jint JNICALL
Java_com_sdfake_app_NativeIo_dropCache(JNIEnv *env, jclass clazz, jint fd) {
    (void) env;
    (void) clazz;
    // posix_fadvise devolve o codigo de erro diretamente (0 = ok)
    return posix_fadvise(fd, 0, 0, POSIX_FADV_DONTNEED);
}
