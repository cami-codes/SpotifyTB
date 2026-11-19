# SpotifyTB

Widget compacto do Spotify para a barra de tarefas do Windows 10/11.

## Recursos

- Capa do álbum, título e artista.
- Anterior, play/pause e próxima.
- Barra de progresso clicável e arrastável.
- Volume por aplicativo: mute, barra de volume e roda do mouse.
- Fundo transparente com contraste suave, sombra no conteúdo e borda opcional.
- Posicionamento ao lado da bandeja ou no canto esquerdo.
- Oculta automaticamente em tela cheia e barra oculta.

## Requisitos

- Windows 10 1809+ ou Windows 11.
- Spotify app desktop.
- .NET 8 Desktop Runtime para o executável leve, ou use a versão autônoma.

## Executar

```powershell
 dotnet run --project SpotifyTB.csproj
```

Compilar executável leve:

```powershell
.\build.bat
```

O arquivo fica em `dist\SpotifyTB.exe`.

Compilar executável autônomo:

```powershell
.\build-standalone.bat
```

O arquivo fica em `dist-standalone\SpotifyTB.exe`.

## Uso

Clique com o botão direito no widget para abrir o menu.

- `Ctrl + arrastar`: muda a posição horizontal.
- Clique ou arraste a barra abaixo do título: muda o tempo da música.
- Clique em `VOL`: ativa/desativa mute do Spotify.
- Arraste a barrinha de volume ou use a roda do mouse: ajusta o volume do Spotify.
- Passe o mouse: mostra música, artista e tempo atual.

As preferências ficam em `%APPDATA%\SpotifyTB\settings.json`.

## Build do GitHub Actions

Pushes e pull requests compilam o projeto. A criação de releases só ocorre em tags `v*` e usa `GITHUB_TOKEN` com a permissão limitada ao job de release. Pull requests não recebem permissão de escrita no repositório.

## Segurança

Consulte as ações antes de contribuir. O projeto não executa scripts PowerShell remotos, não lê/consulta baterias de dispositivos e não envia informações do usuário pela rede. O volume usa sessões locais de áudio do Windows e os metadados usam a API SMTC do Windows.

## Contribuindo

1. Crie um fork.
2. Faça uma branch.
3. Execute `dotnet build SpotifyTB.csproj`.
4. Abra um pull request.

## Licença

MIT
