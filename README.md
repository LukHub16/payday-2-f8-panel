# PAYDAY 2 — Painel F8

Código-fonte de um painel externo em WinForms/WebView2 que mostra a interface web de um trainer local. F8 abre ou oculta o painel. O aplicativo **não implementa os cheats do trainer**; ele se conecta ao servidor local que precisa ser instalado separadamente.

- `Panel.cs`: código do painel.
- `build.ps1`: script local de compilação. Ele espera o SDK WebView2 em `webview2.zip` ou na pasta indicada pelo script; o pacote não está incluído neste repositório.

Este repositório não distribui o trainer Ultimate Trainer, seus scripts, os binários WebView2 nem outros arquivos de terceiros. Use apenas em partidas offline.
