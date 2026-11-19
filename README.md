# SpotifyTB

<p align="center">
  <strong>Um player compacto do Spotify integrado à barra de tarefas do Windows.</strong>
</p>

<p align="center">
  <a href="https://github.com/cami-codes/SpotifyTB/releases/latest"><img alt="Download" src="https://img.shields.io/github/v/release/cami-codes/SpotifyTB?label=download&style=for-the-badge"></a>
  <a href="https://github.com/cami-codes/SpotifyTB/actions/workflows/build.yml"><img alt="Build" src="https://github.com/cami-codes/SpotifyTB/actions/workflows/build.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/github/license/cami-codes/SpotifyTB?style=for-the-badge"></a>
</p>

O SpotifyTB mostra a música que está tocando diretamente sobre a barra de tarefas, sem abrir uma segunda janela. Ele foi feito para ficar discreto sobre barras transparentes e mantém os principais controles ao alcance do mouse.

## Recursos

- Capa do álbum, título e nome do artista.
- Controles anterior, play/pause e próxima.
- Barra de progresso: clique ou arraste para mudar o tempo da música.
- Controle de volume do Spotify separadamente dos outros aplicativos.
- Clique em **VOL** para ativar/desativar o mute.
- Roda do mouse sobre o widget para ajustar o volume em passos de 5%.
- Fundo transparente com sombra suave no texto e destaque nos controles.
- Cor de fundo, posição e borda configuráveis pelo menu de botão direito.
- Suporte ao DPI alto e ocultação automática em tela cheia ou barra oculta.

## Download

Baixe a versão mais recente na página de [Releases](https://github.com/cami-codes/SpotifyTB/releases/latest).

| Arquivo | Quando usar |
|---|---|
| `SpotifyTB-win-x64.exe` | Computador com o [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) já instalado. Arquivo menor. |
| `SpotifyTB-win-x64-standalone.exe` | Não quer instalar dependências. Contém o runtime do .NET e pode ser executado diretamente. |

Coloque o executável em uma pasta fixa e execute. Depois, clique com o botão direito no widget para abrir as opções.

> O Windows SmartScreen pode mostrar um aviso em executáveis ainda sem assinatura digital. Verifique o arquivo baixado antes de executar.

## Uso rápido

- Clique com o **botão direito** no widget para abrir o menu.
- Use **Ctrl + arrastar** para mover o widget horizontalmente.
- Clique ou arraste a barra abaixo do título para buscar dentro da música.
- Clique em **VOL** para silenciar ou reativar o Spotify.
- Clique ou arraste a barra ao lado de **VOL** para ajustar o volume.
- Passe o mouse sobre o widget para ver título, artista e tempo completo.

## Requisitos

- Windows 10 1809 ou superior, ou Windows 11.
- Barra de tarefas horizontal.
- Spotify Desktop aberto e reproduzindo uma música.
- A versão leve precisa do .NET 8 Desktop Runtime.

## Executar pelo código

```powershell
dotnet run --project SpotifyTB.csproj
```

Executável leve:

```powershell
.\build.bat
```

Executável autônomo:

```powershell
.\build-standalone.bat
```

## Configuração

As preferências ficam em `%APPDATA%\SpotifyTB\settings.json`.

| Campo | Padrão | Descrição |
|---|---|---|
| `Anchor` | `"right"` | Posição ao lado da bandeja ou no canto esquerdo. |
| `OffsetX` | `0` | Deslocamento horizontal. |
| `Transparent` | `true` | Mantém o fundo transparente. |
| `ContrastBoost` | `true` | Sombra e destaque no texto e nos controles. |
| `BackColorArgb` | `null` | Cor de fundo escolhida pelo usuário. |
| `BorderColorArgb` | `null` | Cor da borda opcional. |

## Como funciona

- Metadados e controles de mídia são obtidos pela API SMTC do Windows.
- O volume da aplicação Spotify é controlado por uma sessão local de áudio NAudio/Windows.
- O widget é renderizado em uma janela sem borda sempre no topo.
- Nenhuma informação é enviada pela rede pelo aplicativo.

## GitHub Actions e releases

Pushes e pull requests na branch `main` compilam o projeto. Ao criar uma tag no formato `v*`, o workflow publica uma release com os dois executáveis.

```powershell
git tag v1.1.0
git push origin v1.1.0
```

A tag dispara o workflow `.github/workflows/build.yml`, que cria os artefatos leves e autônomos e anexa ambos à release correspondente.

## Segurança e privacidade

- O projeto não executa scripts externos ou PowerShell remoto.
- O Spotify é identificado pela API de mídia do Windows.
- Não coleta telemetria nem envia dados pessoais.
- Pull requests não recebem permissão de escrita no repositório.
- A release usa permissões de conteúdo somente no job responsável pela publicação.

## Contribuindo

1. Faça um fork do repositório.
2. Crie uma branch para sua alteração.
3. Execute `dotnet build SpotifyTB.csproj`.
4. Abra um pull request com uma descrição clara e, quando aplicável, uma captura do visual.

## Licença

[MIT](LICENSE)
