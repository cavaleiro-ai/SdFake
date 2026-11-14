package com.sdfake.app;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.os.storage.StorageManager;
import android.os.storage.StorageVolume;
import android.provider.DocumentsContract;
import android.view.MenuItem;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import com.sdfake.app.engine.Fmt;
import com.sdfake.app.engine.FullResult;

import java.io.File;
import java.util.List;

public final class MainActivity extends Activity {
    private static final int REQ_TREE = 1;
    private static final int REQ_NOTIFICATIONS = 2;

    /** As boas-vindas ja foram vistas nesta abertura do app (vale enquanto o processo viver). */
    private static boolean welcomeSeen;

    private View chooseCard, progressCard, resultCard;
    private ViewGroup volumesBox;
    private TextView targetText, phaseText, detailText, logText;
    private TextView resultTitle, resultSummary, resultLines;
    private ProgressBar bar;
    private Button cancelButton;

    private Uri pendingTree;
    private String pendingName;
    private String pickedVolumeName;
    private int shownState = -1;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable poll = new Runnable() {
        @Override
        public void run() {
            render();
            handler.postDelayed(this, 300);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.main);
        chooseCard = findViewById(R.id.choose);
        progressCard = findViewById(R.id.progress);
        resultCard = findViewById(R.id.result);
        volumesBox = findViewById(R.id.volumes);
        targetText = findViewById(R.id.target);
        phaseText = findViewById(R.id.phase);
        detailText = findViewById(R.id.detail);
        logText = findViewById(R.id.log);
        bar = findViewById(R.id.bar);
        resultTitle = findViewById(R.id.result_title);
        resultSummary = findViewById(R.id.result_summary);
        resultLines = findViewById(R.id.result_lines);
        cancelButton = findViewById(R.id.cancel);

        setupWelcome();
        setupFollow();

        findViewById(R.id.pick).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                openPicker(null);
            }
        });
        cancelButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                confirmCancel();
            }
        });
        findViewById(R.id.again).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                TestRunner.get().reset();
                loadVolumes();
                render();
            }
        });
    }

    // ------------------------------------------------------------------ boas-vindas

    /**
     * Mostra as boas-vindas uma vez a cada abertura do app. Nao aparece se ja ha um teste em
     * andamento ou um resultado esperando (ex.: ao voltar pela notificacao).
     */
    private void setupWelcome() {
        final View welcome = findViewById(R.id.welcome);
        final View content = findViewById(R.id.content);
        boolean show = !welcomeSeen && TestRunner.get().state == TestRunner.IDLE;
        welcome.setVisibility(show ? View.VISIBLE : View.GONE);
        content.setVisibility(show ? View.GONE : View.VISIBLE);

        findViewById(R.id.start).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                welcomeSeen = true;
                welcome.setVisibility(View.GONE);
                content.setVisibility(View.VISIBLE);
            }
        });
        link(R.id.instagram, R.string.url_instagram);
        link(R.id.tiktok, R.string.url_tiktok);
        link(R.id.site, R.string.url_site);
    }

    /** Botao "Seguir" no topo, ao lado do titulo: abre um menu com Instagram, TikTok e site. */
    private void setupFollow() {
        final int[] labels = {R.string.follow_instagram, R.string.follow_tiktok, R.string.follow_site};
        final int[] urls = {R.string.url_instagram, R.string.url_tiktok, R.string.url_site};
        findViewById(R.id.follow).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                PopupMenu menu = new PopupMenu(MainActivity.this, v);
                for (int i = 0; i < labels.length; i++) menu.getMenu().add(0, i, i, labels[i]);
                menu.setOnMenuItemClickListener(new PopupMenu.OnMenuItemClickListener() {
                    @Override
                    public boolean onMenuItemClick(MenuItem item) {
                        open(urls[item.getItemId()]);
                        return true;
                    }
                });
                menu.show();
            }
        });
    }

    /** Botao que abre um endereco no app correspondente (Instagram, TikTok) ou no navegador. */
    private void link(int buttonId, final int urlId) {
        findViewById(buttonId).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                open(urlId);
            }
        });
    }

    private void open(int urlId) {
        String url = getString(urlId);
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, url, Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        loadVolumes();
        handler.removeCallbacks(poll);
        handler.post(poll);
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(poll);
        super.onPause();
    }

    // ------------------------------------------------------------------ escolha do dispositivo

    /** Lista cartoes SD e pendrives que o Android informa; cada um vira um botao. */
    private void loadVolumes() {
        volumesBox.removeAllViews();
        StorageManager sm = (StorageManager) getSystemService(STORAGE_SERVICE);
        List<StorageVolume> volumes = sm.getStorageVolumes();
        int shown = 0;
        for (final StorageVolume v : volumes) {
            if (!v.isRemovable() || !Environment.MEDIA_MOUNTED.equals(v.getState())) continue;
            Button b = (Button) getLayoutInflater().inflate(R.layout.item_volume, volumesBox, false);
            String label = v.getDescription(this);
            long size = volumeSize(v);
            if (size > 0) label += "  ·  " + Fmt.bytes(size);
            b.setText(label);
            b.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View view) {
                    openPicker(v);
                }
            });
            volumesBox.addView(b);
            shown++;
        }
        findViewById(R.id.none).setVisibility(shown == 0 ? View.VISIBLE : View.GONE);
    }

    private static long volumeSize(StorageVolume v) {
        if (Build.VERSION.SDK_INT < 30) return -1;
        File dir = v.getDirectory();
        return dir != null ? dir.getTotalSpace() : -1;
    }

    private void openPicker(StorageVolume volume) {
        Intent intent = null;
        if (volume != null) {
            if (Build.VERSION.SDK_INT >= 29) intent = volume.createOpenDocumentTreeIntent();
            else intent = volume.createAccessIntent(null);
        }
        if (intent == null) intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        pickedVolumeName = volume != null ? volume.getDescription(this) : null;
        try {
            startActivityForResult(intent, REQ_TREE);
            Toast.makeText(this, R.string.picker_hint, Toast.LENGTH_LONG).show();
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, "Este aparelho não tem o seletor de pastas do Android.", Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_TREE || resultCode != RESULT_OK || data == null || data.getData() == null) return;
        final Uri tree = data.getData();

        String problem = validate(tree);
        if (problem != null) {
            new AlertDialog.Builder(this).setTitle("Local não permitido").setMessage(problem)
                    .setPositiveButton("Entendi", null).show();
            return;
        }
        try {
            getContentResolver().takePersistableUriPermission(tree,
                    Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        } catch (RuntimeException ignored) {
            // a permissao temporaria ja basta enquanto o app estiver aberto
        }

        final String name = describe(tree);
        new AlertDialog.Builder(this)
                .setTitle("Testar " + name + "?")
                .setMessage(R.string.confirm_text)
                .setNegativeButton("Agora não", null)
                .setPositiveButton("Começar", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        begin(tree, name);
                    }
                })
                .show();
    }

    /** So aceita cartao SD ou pendrive: nunca a memoria interna, nuvem ou Downloads. */
    private static String validate(Uri tree) {
        if (!SafStore.AUTHORITY.equals(tree.getAuthority()))
            return "Escolha uma pasta dentro do cartão SD ou do pendrive (não vale nuvem, Downloads ou outros apps).";
        String volume = volumeId(tree);
        if (volume == null || "primary".equalsIgnoreCase(volume) || "home".equalsIgnoreCase(volume))
            return "Esse local é a memória interna do celular. Escolha o cartão SD ou o pendrive: "
                    + "no seletor, abra o menu lateral (☰) e toque no nome do cartão.";
        return null;
    }

    private static String volumeId(Uri tree) {
        try {
            String id = DocumentsContract.getTreeDocumentId(tree);
            int colon = id.indexOf(':');
            return colon > 0 ? id.substring(0, colon) : null;
        } catch (RuntimeException e) {
            return null;
        }
    }

    private String describe(Uri tree) {
        String id = volumeId(tree);
        StorageManager sm = (StorageManager) getSystemService(STORAGE_SERVICE);
        for (StorageVolume v : sm.getStorageVolumes()) {
            String uuid = v.getUuid();
            if (uuid != null && uuid.equalsIgnoreCase(id)) return v.getDescription(this);
        }
        return pickedVolumeName != null ? pickedVolumeName : "o dispositivo " + id;
    }

    // ------------------------------------------------------------------ teste

    private void begin(Uri tree, String name) {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            // Pede para mostrar o andamento nas notificacoes; o teste comeca qualquer que seja a resposta.
            pendingTree = tree;
            pendingName = name;
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, REQ_NOTIFICATIONS);
            return;
        }
        launch(tree, name);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQ_NOTIFICATIONS && pendingTree != null) {
            Uri tree = pendingTree;
            pendingTree = null;
            launch(tree, pendingName);
        }
    }

    private void launch(Uri tree, String name) {
        if (!TestRunner.get().start(getApplicationContext(), tree, name)) return;
        try {
            TestService.start(this);
        } catch (RuntimeException ignored) {
            // sem servico em primeiro plano o teste ainda roda enquanto o app estiver aberto
        }
        render();
    }

    private void confirmCancel() {
        new AlertDialog.Builder(this)
                .setMessage("Cancelar o teste? Os arquivos de teste serão apagados do cartão.")
                .setNegativeButton("Continuar testando", null)
                .setPositiveButton("Cancelar teste", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        TestRunner.get().cancel();
                        render();
                    }
                })
                .show();
    }

    // ------------------------------------------------------------------ tela

    private void render() {
        TestRunner r = TestRunner.get();
        int state = r.state;
        if (state != shownState) {
            shownState = state;
            chooseCard.setVisibility(state == TestRunner.IDLE ? View.VISIBLE : View.GONE);
            progressCard.setVisibility(state == TestRunner.RUNNING ? View.VISIBLE : View.GONE);
            resultCard.setVisibility(state == TestRunner.DONE ? View.VISIBLE : View.GONE);
            if (state == TestRunner.RUNNING) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            if (state == TestRunner.DONE) showResult(r.result);
        }
        if (state == TestRunner.RUNNING) {
            boolean cancelling = r.cancelling();
            targetText.setText("Testando " + r.target);
            phaseText.setText(cancelling ? "Cancelando e limpando... aguarde." : r.phase + "  —  " + Fmt.percent(r.fraction));
            detailText.setText(r.detail);
            bar.setProgress((int) (r.fraction * 1000));
            cancelButton.setEnabled(!cancelling);
            String log = r.logText();
            logText.setText(log);
            logText.setVisibility(log.length() > 0 ? View.VISIBLE : View.GONE);
        }
    }

    private void showResult(FullResult result) {
        if (result == null) return;
        int fill, fore;
        String mark;
        switch (result.verdict) {
            case FullResult.GENUINE: fill = R.color.ok_bg; fore = R.color.ok_fg; mark = "✔  "; break;
            case FullResult.FAKE: fill = R.color.bad_bg; fore = R.color.bad_fg; mark = "✖  "; break;
            case FullResult.DEFECTIVE: fill = R.color.warn_bg; fore = R.color.warn_fg; mark = "⚠  "; break;
            case FullResult.UNCONFIRMED: fill = R.color.warn_bg; fore = R.color.warn_fg; mark = "?  "; break;
            case FullResult.INCOMPLETE: fill = R.color.neutral_bg; fore = R.color.ink; mark = ""; break;
            default: fill = R.color.warn_bg; fore = R.color.warn_fg; mark = ""; break;
        }
        if (result.suspiciouslySmall()) {
            fill = R.color.warn_bg;
            fore = R.color.warn_fg;
            mark = "⚠  ";
        }
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(getColor(fill));
        bg.setCornerRadius(14 * getResources().getDisplayMetrics().density);
        resultCard.setBackground(bg);
        int color = getColor(fore);
        resultTitle.setTextColor(color);
        resultSummary.setTextColor(color);
        resultLines.setTextColor(color);
        resultTitle.setText(mark + result.title());
        resultSummary.setText(result.summary());
        StringBuilder sb = new StringBuilder();
        for (String line : result.lines()) {
            if (sb.length() > 0) sb.append('\n');
            sb.append("•  ").append(line);
        }
        resultLines.setText(sb);
    }
}
