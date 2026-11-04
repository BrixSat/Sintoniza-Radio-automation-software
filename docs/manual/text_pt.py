"""O texto do manual do utilizador, em português (build.py junta tudo). Os blocos são os de text_en.py; os nomes dos
botões e menus são os do programa em português (src/Playout.App/i18n/pt.json)."""

def _date(d):
    months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"]
    return f"{d.day} de {months[d.month - 1]} de {d.year}"

WORDS = {
    "title": "Sintoniza",
    "subtitle": "Manual do utilizador",
    "contents": "Índice",
    "tip": "Dica",
    "important": "Importante",
    "version": "Versão",
    "date": "%d/%m/%Y",
    "format_date": _date,
}

CHAPTERS = [
("O Sintoniza num relance", "intro", [
    ("p", "O Sintoniza é um sistema de automação para rádios que estão no ar a toda a hora. Este capítulo apresenta os "
          "quatro programas que o compõem e mostra, em quatro passos, como um dia de emissão é preparado e vai para o ar."),
    ("p", "Três dos programas partilham uma base de dados a que chamamos <b>biblioteca</b>. É lá que vivem a música e as "
          "suas categorias, a programação, as playlists diárias e o histórico de tudo o que foi emitido."),
    ("icons", [
        ("radio-playout.png", "Sintoniza Emi", "O programa de emissão, no estúdio. É o que o locutor tem à frente: a playlist, o cartwall, o microfone e o stream."),
        ("radio-playout-schedule.png", "Sintoniza Pro", "O programa de produção. Aqui se definem a estação, as categorias e as cartwalls, os eventos, a publicidade, os programas e as playlists de cada dia."),
        ("radio-playout-import.png", "Sintoniza Import", "Traz pastas de música para a biblioteca, já arrumadas em categorias."),
        ("radio-playout-rec.png", "Sintoniza Rec", "Grava tudo o que a estação emite e guarda as gravações durante o tempo que a lei pede. Trabalha sozinho, sem a biblioteca."),
    ]),
    ("p", "Cada programa tem a sua entrada no menu das aplicações em Linux, no menu Iniciar e no ambiente de trabalho em "
          "Windows, e em Aplicações no macOS. Só corre uma cópia de cada programa por computador."),
    ("p", "Com uma biblioteca MySQL ou MariaDB partilhada, a Pro e o Import podem trabalhar noutros computadores, por "
          "exemplo no departamento comercial ou na discoteca. O estúdio vê as alterações em poucos segundos."),
    ("tip", "As <b>Definições</b> do programa de emissão só guardam o que é desse computador: as placas de som, o microfone, "
            "a base de dados e as teclas. <b>Tudo o que é da estação</b>, como o nome e o logótipo, os crossfades, a música de "
            "enchimento, o sinal horário, o streaming, o RDS, o site, os alertas, as categorias e as cartwalls, <b>define-se uma "
            "só vez na Sintoniza Pro</b> e chega a todos os estúdios."),
    ("h2", "Como o dia é planeado e emitido"),
    ("steps", [
        "<b>Importe</b> a música para categorias, debaixo das oito categorias da estação: Music › Atuais, Gold, Anos 80, Jingles, Spots e as que precisar.",
        "Na <b>Pro</b>, crie <b>modelos de hora</b> para os seus programas, que dizem que categorias tocam e por que ordem, e coloque-os na semana. Marque também os <b>blocos publicitários</b> e os anúncios dos <b>clientes</b>.",
        "A partir dos modelos são feitas as <b>playlists diárias</b> de hoje e dos dias seguintes. Pode revê-las e trocar músicas à mão, ou deixar que as regras as preencham.",
        "O <b>programa de emissão</b> toca as playlists diárias, lança os blocos e os eventos à hora marcada e garante que cada hora começa a horas.",
    ]),
]),

("Primeira configuração", "setup", [
    ("p", "Este capítulo explica o que tem de configurar antes de ir para o ar: primeiro em cada computador de estúdio, "
          "depois uma única vez para toda a estação."),
    ("h2", "Em cada computador de estúdio"),
    ("p", "No programa de emissão, clique em <b>Definições</b>, no canto inferior esquerdo, ou use Ctrl/Cmd + vírgula. "
          "Aqui só está o que diz respeito a esse computador, e nada muda até carregar em <b>Guardar</b>."),
    ("p", "Quando a biblioteca já está configurada, as Definições pedem um utilizador com permissão para mexer neste "
          "computador. Numa instalação nova, entre com <b>admin</b> e a palavra-passe <b>admin</b>."),
    ("fig", "settings-station", "Definições → Geral", [("LanguageBox", "O <b>idioma</b> dos ecrãs: English, Português ou Français. A mudança vale a partir da próxima vez que o programa arrancar.")]),
    ("p", "<b>Outros idiomas.</b> Precisa do programa noutra língua, ou quer corrigir uma tradução? No mesmo separador, "
          "<b>Adicionar um idioma…</b> abre a pasta das traduções. Um ficheiro posto lá com o nome da língua, como de.json "
          "ou es.json, acrescenta esse idioma à lista, sem ser preciso uma nova versão do programa. Um ficheiro pt.json "
          "corrige ou completa o português que já vem com o programa. Os textos que faltarem aparecem em inglês."),
    ("fig", "settings-audio", "Definições → Áudio", [
        ("MusicDeviceBox", "A placa de som e o par de canais por onde sai a <b>Música</b>. Por baixo escolhe as saídas do Cartwall e da Pré-escuta. <b>Testar</b> toca um tom curto, para descobrir que fader da mesa é qual."),
        ("PreviewDeviceBox", "A <b>Pré-escuta</b> serve para ouvir antes de ir para o ar. Ligue-a aos auscultadores ou ao PFL da mesa, nunca a uma saída que vá para o ar.")]),
    ("fig", "settings-library", "Definições → Biblioteca", [
        ("LibraryTypeBox", "Onde fica a biblioteca. <i>SQLite</i> é um ficheiro neste computador; <i>MySQL / MariaDB</i> é um servidor partilhado por vários estúdios."),
        ("TestLibraryButton", "<b>Testar</b> abre a biblioteca. Da primeira vez, cria também as tabelas.")]),
    ("p", "<b>Música partilhada entre Windows, Mac e Linux.</b> Numa biblioteca partilhada, cada música fica registada com o "
          "caminho que o computador que a analisou vê, por exemplo <i>/mnt/radio/Pop/musica.mp3</i> num estúdio Linux. Um "
          "computador Windows ou Mac vê a mesma partilha noutro sítio, como <i>Z:\\</i>, <i>\\\\servidor\\radio</i> ou "
          "<i>/Volumes/radio</i>. Basta dizer-lho uma vez, mais abaixo no separador Biblioteca."),
    ("fig", "settings-paths", "Definições → Biblioteca → Caminhos deste computador", [
        ("PathMapRows", "Em <b>Na biblioteca</b> fica o início dos caminhos tal como estão na biblioteca. Em <b>neste computador</b> fica onde essa pasta está aqui. <b>Pasta…</b> ajuda a escolhê-la."),
        ("AddPathMapButton", "<b>Adicionar</b> junta uma linha e sugere logo onde começam as músicas da biblioteca."),
        ("TestPathMapButton", "<b>Testar</b> procura algumas músicas da biblioteca neste computador…"),
        ("PathMapNote", "…e diz quantas encontrou. Se alguma faltar, mostra onde a procurou. No fim, carregue em <b>Guardar</b>."),
    ], True),
    ("tip", "A partir daí, esse computador abre cada ficheiro onde ele realmente está, seja para tocar, ouvir, desenhar formas de "
            "onda, editar etiquetas ou gravar voice tracks. O que ele acrescentar fica guardado com o caminho da biblioteca, para "
            "todos os estúdios o encontrarem. A Pro, o Rec e o Import desse computador usam a mesma definição. A partilha tem de "
            "aparecer como uma pasta: no Windows, uma unidade mapeada ou <i>\\\\servidor\\partilha</i>; no Linux, montada em /mnt, "
            "e não um endereço <i>smb://</i> do gestor de ficheiros; no Mac, em /Volumes."),
    ("fig", "settings-safety", "Definições → Segurança", [
        ("ResumeBox", "Para uma estação que nunca sai do ar, ligue <b>Continuar a tocar ao arrancar, se estiver em AUTO</b>, <b>Voltar a arrancar se falhar</b> e <b>Arrancar quando inicio sessão</b>."),
        ("BackupBox", "<b>Cópias de segurança</b> diárias da base de dados e dos ficheiros da estação.")]),
    ("h2", "Uma vez para toda a estação"),
    ("p", "O resto define-se na <b>Sintoniza Pro</b>, em <b>Definições da estação</b>. Entre como <b>admin</b> com a "
          "palavra-passe <b>admin</b> e, antes de mais nada, mude essa palavra-passe em Estação → Utilizadores."),
    ("fig", "station-station", "Definições da estação → Estação", [("StationNameBox", "O <b>nome da estação</b>, o <b>logótipo</b>, o género e o site. Aparecem ao lado do relógio e seguem para o stream e o RDS. "
        "Por baixo, o <b>fuso horário</b>: onde a estação está no ar. Deixe <i>O de cada computador</i> quando os relógios dos estúdios estão na hora da estação; "
        "escolha um quando um estúdio ou um servidor está noutro sítio, e a programação, o sinal horário, o relógio, os registos e os relatórios seguem a hora da estação. "
        "Cada programa só passa a usar uma alteração quando voltar a arrancar (a Emi e a Pro avisam); as cópias de segurança seguem a hora do computador. "
        "Em <b>Tipo de estação</b>, escolha <i>Rádio FM</i> se o microfone passa pela mesa de mistura, ou <i>Rádio web</i> se passa pelo computador.")]),
    ("fig", "station-audio", "Definições da estação → Áudio", [("CrossfadeBox", "Quantos segundos uma música se cruza com a seguinte."),
        ("OtherCrossfadeBox", "O crossfade à entrada e à saída de jingles, anúncios e programas. Com 0, a música acaba e o jingle entra limpo."),
        ("LoudnessBox", "Põe tudo ao mesmo volume percebido, segundo a norma EBU R128.")]),
    ("fig", "station-safety", "Definições da estação → Segurança", [
        ("SilenceBox", "<b>Detetar silêncio</b>: se nada se ouvir durante algum tempo, soa um alarme e, em AUTO, o programa salta para o item seguinte."),
        ("FillBox", "A <b>música de enchimento</b> toca se a playlist alguma vez acabar. Aqui também se definem os tempos de descanso: quantas horas até a mesma música voltar e quantos minutos até o mesmo artista voltar. As playlists diárias respeitam os mesmos tempos.")]),
    ("p", "Os restantes separadores são o <b>Sinal horário</b>, a <b>Biblioteca</b> com outras pastas de música, o "
          "<b>Streaming</b>, os textos do <b>RDS</b>, o <b>Site</b>, que tem secção própria no capítulo da Sintoniza Pro, os "
          "<b>Alertas</b> e a <b>Playlist</b>, com quantos dias de playlists se fazem com antecedência e as cores das linhas."),
    ("p", "Depois de guardar, todos os estúdios recebem as definições da estação no espaço de um minuto. Depois de uma "
          "atualização, o primeiro estúdio a arrancar com a nova versão publica as suas."),
    ("tip", "A seguir, importe a música (capítulo 5), crie as cartwalls, os modelos de hora e os blocos (capítulo 4), ponha o programa de emissão em <b>AUTO</b> e carregue em <b>Play</b>."),
]),

("O programa de emissão", "onair", [
    ("p", "O <b>Sintoniza Emi</b> é o programa com que o locutor trabalha no estúdio. Este capítulo percorre o ecrã de "
          "emissão, a playlist, o cartwall e a biblioteca, e depois o que liga o programa ao resto do estúdio: a mesa, o "
          "controlo remoto e os computadores de reserva."),
    ("p", "A metade de cima do ecrã mostra o que está no ar agora. A metade de baixo mostra o que vem a seguir, na playlist, "
          "e o que está à mão: o cartwall, a biblioteca e as pastas."),
    ("h2", "Entrar"),
    ("p", "O programa abre <b>bloqueado</b>. O ecrã de emissão vê-se por trás do quadro de entrada, mas nada responde a "
          "cliques nem a teclas até entrar um utilizador com a permissão <b>No ar</b>."),
    ("p", "Bloqueado ou não, a estação continua no ar. Em <b>AUTO</b> o programa começa a tocar sozinho quando arranca, a "
          "programação e os blocos publicitários seguem o seu curso e o controlo remoto web funciona."),
    ("fig", "main-locked", "Bloqueado: entre para trabalhar no ar", [
        ("UserBox", "O utilizador. O último a entrar já vem preenchido…"),
        ("PasswordBox", "…e a palavra-passe."),
        ("LoginButton", "<b>Entrar</b>, ou a tecla Enter."),
        ("LockNote", "Enquanto isso, o que está no ar continua a tocar."),
    ]),
    ("tip", "<b>Sair</b>, em baixo à esquerda, volta a bloquear o ecrã, por exemplo entre dois programas. Bloqueada, a janela não se fecha. "
            "Se a biblioteca não estiver acessível não há utilizadores para verificar, e o programa abre sem pedir entrada para a estação poder continuar a trabalhar."),
    ("h2", "O ecrã"),
    ("fig", "main", "O ecrã de emissão", [
        ("OnAirBadge", "O estado da emissão: <b>NO AR</b>, <b>PARADO</b>, <b>SILÊNCIO</b> quando nada se ouve, ou a contagem para o próximo bloco."),
        ("NowTitle", "O que está no ar, com o artista."),
        ("WaveMain", "A forma de onda. A parte vermelha já tocou. A barra fina por baixo mostra a música inteira."),
        ("Elapsed", "O tempo decorrido e, por baixo, o que vem a <b>SEGUIR</b>."),
        ("NoteBox", "As <b>notas para o locutor</b> que vêm das playlists diárias: a do item no ar e a do seguinte. Servem para lembrar o que anunciar ou o patrocinador a referir."),
        ("PlayButton", "<b>Play</b>, ou F5."),
        ("PauseButton", "<b>Pausa</b> e continuar, ou F6."),
        ("NextButton", "<b>Seguinte</b>, ou F7, faz já o crossfade para o próximo item."),
        ("StopButton", "<b>Parar</b>, ou F8."),
        ("Remaining", "O tempo que falta ao que está no ar."),
        ("AutoSwitch", "Em <b>AUTO</b> o programa segue a playlist sozinho. Em <b>ASSISTIDO</b> para no fim de cada item e espera que o locutor lance o seguinte. Em ASSISTIDO cada música da playlist tem um <b>▶</b> verde para a pôr logo no ar, e a música no ar um botão cor de laranja para a <b>desvanecer</b>. As já tocadas não têm (um duplo clique continua a tocá-las de novo)."),
        ("Clock", "O relógio, com o nome e o logótipo da estação."),
        ("TopOfHourText", "A contagem para a hora certa."),
        ("PlaylistEndsText", "A hora a que a playlist acaba."),
        ("NextEventPanel", "O <b>PRÓXIMO EVENTO</b> é a próxima coisa com hora marcada, seja um evento, um bloco ou um item com hora. A contagem fica âmbar no último minuto e vermelha nos últimos 10 segundos."),
        ("MeterL", "Os níveis de saída, esquerdo e direito."),
        ("Playlist", "A playlist, explicada nas páginas seguintes."),
        ("CartwallTab", "Os separadores <b>CARTWALL</b>, <b>BIBLIOTECA</b> e <b>PASTAS</b>."),
        ("Cartwall", "O cartwall, com os jingles e sons à mão."),
        ("SettingsButton", "<b>Definições</b>, ou Ctrl/Cmd + vírgula."),
        ("ReportsButton", "<b>Relatórios</b>: a música emitida e o comprovativo de emissão."),
        ("LogoutButton", "<b>Sair</b> volta a bloquear o ecrã, e a emissão continua."),
        ("Lights", "As <b>luzes de estado</b>, cada uma com o seu nome. Verde quer dizer que está a funcionar, âmbar um aviso, vermelho um problema e cinzento desligado. Aponte para uma luz para saber o que se passa e clique nela para abrir as suas definições."),
        ("LastLog", "A última coisa que aconteceu. Fica tudo registado no registo do dia."),
    ]),
    ("h2", "As luzes de estado e o sinal NO AR"),
    ("p", "Num relance, o locutor ou o técnico deve conseguir saber se está tudo bem. Para isso servem as luzes de estado na "
          "barra de baixo e o sinal vermelho no painel da estação."),
    ("p", "Cada luz diz respeito a uma parte da emissão. As cores são sempre as mesmas: <b>verde</b> está a funcionar, "
          "<b>âmbar</b> é um aviso, <b>vermelho</b> é um problema e <b>cinzento</b> quer dizer que essa parte está desligada ou "
          "não é usada neste computador. Eis o que cada uma vigia:"),
    ("table", ["Luz", "O que é", "O que as cores dizem"], [
        ["ESTÚDIO", "Só aparece quando há mais de um computador para a estação: qual deles está no ar.",
         "Verde: este computador está no ar (e diz quantos estão de reserva). Âmbar: este está de reserva, outro está no ar."],
        ["STREAM", "O stream da rádio na internet, que este computador envia para o servidor Icecast ou Shoutcast.",
         "Verde: a enviar, com há quanto tempo. Âmbar: a ligar ao servidor. Vermelho: não consegue ligar. Cinzento: desligado ou não configurado."],
        ["OUVINTES", "Quantas pessoas estão a ouvir o stream, lido do servidor a cada minuto. O número aparece ao lado da luz.",
         "Verde: o número de agora. Âmbar (?): não consegue ler o servidor, ou deixou de contar. Cinzento: a contagem está desligada."],
        ["RDS", "O texto que aparece nos rádios FM (o nome da estação e a música), enviado para o codificador RDS.",
         "Verde: a enviar. Vermelho: o codificador não responde. Cinzento: o RDS está desligado, ou este computador é de reserva."],
        ["SITE", "O \"a tocar agora\" do site da estação: a música no ar enviada para o site, para ficheiros ou por FTP.",
         "Verde: enviado. Vermelho: não consegue enviar (o site ou o servidor FTP não responde). Cinzento: desligado, ou de reserva."],
        ["REC", "O Sintoniza Rec, o programa que grava a emissão (a gravação que a lei pede), neste computador.",
         "Verde: a gravar. Âmbar: está a gravar silêncio. Vermelho: está aberto mas NÃO grava. Cinzento: não está a correr."],
        ["BIBLIOTECA", "A ligação à base de dados com as músicas, as playlists e as definições da estação.",
         "Verde: aberta. Vermelho: não responde (a emissão continua com o que já tem). Cinzento: não há biblioteca."],
        ["PLAYLIST DE HOJE", "As playlists diárias das horas que faltam hoje, feitas no Sintoniza Pro.",
         "Verde: todas feitas. Âmbar: falta alguma mais tarde. Vermelho: falta a desta hora ou da próxima. Cinzento: não há modelos de hora."],
        ["SILÊNCIO", "A deteção de silêncio: o ar sem som durante demasiado tempo.",
         "Verde: a vigiar. Vermelho: há silêncio no ar (em AUTO passa ao item seguinte). Cinzento: a deteção está desligada."],
        ["MIC", "O microfone, pelo botão MIC ou pelo fader do micro da mesa (por GPIO).",
         "Vermelho: o microfone está aberto, no ar. Cinzento: fechado."],
        ["GPIO", "A ligação à mesa de mistura: os faders que põem a tocar e as luzes da mesa.",
         "Verde: pronta. Vermelho: alguma ligação não abre (o registo diz porquê). Cinzento: não há GPIO configurado."],
        ["ALERTAS", "Os avisos por Telegram, e-mail ou web quando algo corre mal (silêncio, stream em baixo, disco cheio…).",
         "Verde: ligados, está tudo bem. Vermelho: há um problema por resolver (aponte para ver qual). Cinzento: desligados."],
    ]),
    ("p", "Aponte para uma luz para ler, por extenso, o que se passa. Clique nela para ir direto às suas definições."),
    ("p", "O <b>sinal NO AR</b> acende-se a vermelho no painel da estação, entre o relógio e os medidores de nível. Diz "
          "<b>MIC LIVE</b> enquanto o microfone está aberto, seja pelo botão MIC, seja pelo fader do micro da mesa através do "
          "GPIO. Durante um programa em direto diz <b>NO AR · </b> seguido do nome do programa, e enquanto toca uma entrada em "
          "direto diz <b>NO AR · ENTRADA EM DIRETO</b>. Ocupa um espaço que de outro modo fica vazio, por isso nada no ecrã "
          "salta quando ele acende."),
    ("fig", "main-onair", "O microfone aberto, com os contadores grandes", [
        ("OnAirSign", "O sinal <b>MIC LIVE</b>: aqui, o fader do micro da mesa está em cima."),
        ("Remaining", "Com os <b>contadores grandes</b>, o tempo que falta e os pontos de cue leem-se do outro lado do estúdio…"),
        ("NextEventCountdown", "…tal como a contagem para o próximo evento e para a hora certa."),
        ("Lights", "As luzes de estado, na barra de baixo, como vêm por omissão. O MIC está vermelho porque o microfone está aberto."),
    ]),
    ("p", "Cada computador escolhe o que mostra, em <b>Definições → Ecrã</b>. Um estúdio pode querer as luzes num painel "
          "maior, com texto, e outro preferir o ecrã mais limpo."),
    ("fig", "settings-screen", "Definições → Ecrã", [
        ("StatusPanelBox", "<b>Luzes de estado num painel por baixo do leitor</b>: em vez das luzes pequenas na barra, cada luz aparece por baixo do leitor com o que está a fazer, por extenso."),
        ("LampBoxes", "<b>Luzes mostradas</b>: desmarque as que não interessam neste estúdio, como o RDS numa rádio web. O que cada luz quer dizer está na tabela das luzes de estado, acima."),
        ("BigCountersBox", "<b>Contadores grandes</b>: o tempo que falta, os pontos de cue, o próximo evento e a hora certa em números grandes."),
        ("OnAirSignBox", "O <b>sinal NO AR</b>. Desmarque-o para nunca o mostrar neste computador."),
    ]),
    ("tip", "Quer uma luz verdadeira por cima da porta do estúdio? Uma saída GPIO pode acender-se ao mesmo tempo que o sinal NO AR, e uma entrada GPIO pode dizer ao programa que o fader do micro da mesa subiu. Veja a secção sobre GPIO, mais à frente."),
    ("h2", "A playlist"),
    ("p", "Cada linha mostra um ícone com o tipo de item: ♫ para música, ✦ para jingle, € para anúncio, 🎙 para programa, e "
          "outro para streams. Vêm depois o título e o artista, a hora prevista de <b>início</b> e a <b>duração</b>."),
    ("p", "Tudo o que não é música tem a sua cor de fundo, que se muda em Definições da estação → Playlist. O item no ar vai-se "
          "enchendo de vermelho à medida que toca, e uma linha marca o início de cada hora."),
    ("fig", "main", "Botões da playlist", [
        ("OnAirButton", "<b>No ar</b> volta ao item que está a tocar. Acende-se quando se afasta dele."),
        ("HourViewButton", "<b>Esta hora</b> mostra a hora anterior, a hora atual e a primeira meia hora da seguinte. Desligado, mostra tudo o que falta tocar."),
        ("AddStreamButton", "<b>Stream…</b> junta o stream de outra rádio, ou uma entrada deste computador em direto: a linha da mesa, um recetor de satélite, um estúdio remoto. Ao lado ficam <b>Pausa…</b>, <b>Pedidos…</b>, <b>Vista do locutor</b>, <b>Abrir…</b> e <b>Guardar…</b> para playlists M3U, e ▲ ▼ para mover itens."),
        ("RemoveButton", "<b>Remover</b> tira os itens selecionados."),
        ("ClearButton", "<b>Limpar</b> esvazia a playlist, menos o que está no ar e o que a programação bloqueou. O plano volta a enchê-la."),
    ], True),
    ("list", [
        "Para pôr um item no ar já, faça <b>duplo clique</b> nele ou carregue em Enter.",
        "Para mudar a ordem, <b>arraste</b> as linhas. Também pode largar músicas vindas da biblioteca, do separador Pastas ou do gestor de ficheiros.",
        "Com o <b>botão direito</b> num item tem <i>Editar pontos de cue…</i> (Ctrl/Cmd+E), <i>Editar etiquetas…</i> (Ctrl/Cmd+T) e <i>Remover</i>.",
        "As linhas marcadas <b>PROGRAMADO</b> são blocos e eventos que aí vêm, mostrados no sítio onde vão tocar.",
        "Uma música que só começaria depois de acabar a sua hora aparece como <b>NÃO TOCA</b>. Nos últimos minutos da hora fica de fora, para a hora seguinte começar a horas.",
    ]),
    ("warn", "<b>Os anúncios estão protegidos.</b> Enquanto um anúncio está no ar, Seguinte, Pausa, Parar ou pôr outro item no ar "
             "perguntam primeiro, e só um clique no botão da pergunta o faz. Assim um spot pago não é cortado por engano, mas "
             "numa emergência ainda pode ser."),
    ("p", "<b>De onde vêm as músicas?</b> Das playlists diárias, preparadas na Sintoniza Pro (capítulo 4). Cerca de meia hora "
          "antes de cada hora, as músicas dessa hora entram na playlist."),
    ("p", "Perto do fim da hora, o programa acerta o horário. Se a hora seguinte fosse começar atrasada, deixa de fora um item "
          "marcado como <i>opcional</i>. Se fosse começar adiantada, junta músicas. O locutor pode sempre mexer na playlist "
          "no ar; o plano fica como estava e o registo de emissão guarda o que tocou de facto."),
    ("h2", "Pausas na playlist"),
    ("p", "Às vezes o locutor precisa de falar sem música por baixo: uma entrevista curta, a leitura de um comunicado. "
          "<b>Pausa…</b>, por cima da playlist, junta um item sem som, que dura um tempo marcado, por exemplo 1:30 para minuto "
          "e meio, ou até carregar em <b>Seguinte</b>."),
    ("p", "O título da pausa é o assunto da conversa. Aparece na playlist, no ar e nas notas quando a pausa é a seguinte, mas "
          "nunca vai para o RDS, o site nem o stream. Não há crossfade a entrar nem a sair, e a deteção de silêncio deixa-a em "
          "paz. Para a mudar antes de tocar, use o botão direito e <i>Editar pausa…</i>."),
    ("h2", "Programas em direto e a vista do locutor"),
    ("p", "Quando um programa ao vivo marcado na Pro começa (capítulo 4, Programas), o nome do programa e do locutor seguem "
          "para o RDS e para o site, mas a emissão continua em AUTO até o locutor dizer que está. Se alguém tiver sessão "
          "iniciada no ar, uma barra azul <b>PROGRAMA</b> por cima da playlist e a <b>vista do locutor</b>, que se abre sozinha, "
          "perguntam se é quem o apresenta. <b>Estou aqui: ASSISTIDO</b> passa para <b>ASSISTIDO</b>, e cada item espera que o "
          "locutor carregue em Play; <b>Manter AUTO</b> deixa o programa correr sozinho. Se ninguém tiver sessão iniciada, nada "
          "muda até um locutor entrar, e aí é-lhe perguntado."),
    ("p", "Se 2 minutos depois do início ninguém tiver dito que apresenta o programa, o registo diz isso e, se estiver "
          "configurado em Definições → Alertas, é enviado um alerta. A estação continua a tocar em AUTO: um programa a que "
          "ninguém apareceu nunca deixa silêncio no ar. Terminar a sessão durante o programa volta a AUTO, e a playlist "
          "arranca se estava à espera. Quando o programa acaba, a emissão volta a <b>AUTO</b> e, se nada estiver a tocar, "
          "arranca a playlist. Os blocos publicitários e os eventos tocam como sempre. Um programa <b>automático</b> nunca "
          "pergunta: só dá nome a essa parte do dia."),
    ("p", "A <b>vista do locutor</b> é um ecrã grande e simples, pensado para quem está a fazer o programa. Mostra quanto falta "
          "para o programa acabar, o que está no ar com o tempo que falta e a contagem <b>FALAR ATÉ À VOZ</b> até ao fim da "
          "introdução, os três itens seguintes, as notas, as contagens para o próximo bloco e para a hora certa, os pedidos à "
          "espera, e botões grandes para PLAY, SEGUINTE e PARAR. Também se abre a qualquer momento com <b>Vista do locutor</b>, "
          "por cima da playlist."),
    ("h2", "Pedidos dos ouvintes"),
    ("p", "Quem atende o telefone ou lê as mensagens regista os pedidos para o locutor. Em <b>Pedidos…</b>, por cima da "
          "playlist, procure a música na biblioteca e escreva para quem é e a mensagem do ouvinte. No controlo remoto web, o "
          "botão ♥ faz o mesmo a partir de um telemóvel."),
    ("p", "O locutor vê os pedidos à espera e decide: põe cada um a tocar a seguir, e a dedicatória passa a ser a nota desse "
          "item no ar, ou recusa-o. A lista é só do estúdio e nada fica aberto à internet."),
    ("h2", "Cartwall"),
    ("fig", "main", "O cartwall", [
        ("Cartwall", "Cada cart tem nome, ícone e cor. O número é a tecla do cart, definida em Definições → Atalhos."),
        ("CartwallPicker", "A cartwall que está a ser mostrada."),
    ], True),
    ("list", [
        "<b>Clique</b> num cart para o tocar e outra vez para o parar. Os carts tocam por cima da música, na saída do Cartwall. Cada um mostra quanto dura e, enquanto toca, quanto lhe falta.",
        "As cartwalls são da estação. Fazem-se na Sintoniza Pro, em <b>Cartwalls</b>, e chegam a todos os estúdios no espaço de um minuto.",
        "Pode dar uma tecla a cada cart em Definições → Atalhos.",
    ]),
    ("h2", "Biblioteca"),
    ("fig", "main-library", "O separador Biblioteca (Ctrl/Cmd + F)", [
        ("SearchBox", "Procure em todas as músicas enquanto escreve: artista, título, álbum ou nome do ficheiro. Maiúsculas e acentos não contam."),
        ("CategoryBox", "Mostra só uma categoria, incluindo as que estão dentro dela. O botão ✎ ao lado edita as categorias."),
        ("TrackList", "As músicas. Com <b>duplo clique</b> ou Enter, a música vai para o fim da playlist. O <b>botão direito</b> tem Tocar a seguir, Categoria, Também em, Andamento, Voz, Idioma, Fora do site, Editar etiquetas… e Editar pontos de cue…"),
        ("ScanButton", "<b>Analisar</b> põe já a biblioteca em dia com as pastas de música."),
    ]),
    ("fig", "main-folders", "O separador Pastas", [("Library", "As pastas de música do disco. Arraste ficheiros ou pastas para a playlist. Com o botão direito numa pasta, <i>Categoria</i> põe todas as músicas dela numa categoria, e também as que lá forem postas mais tarde.")]),
    ("h2", "Pontos de cue"),
    ("fig", "cue-editor", "Editar pontos de cue…", []),
    ("p", "Os pontos de cue dizem ao programa onde cada ficheiro começa, onde acaba e onde o locutor pode falar por cima. "
          "Marque o <b>Cue In</b>, onde a música começa; a <b>Intro</b>, onde acaba a introdução; o <b>Outro</b>; o <b>Mix</b>, "
          "onde entra o item seguinte; e o <b>Cue Out</b>, onde a música para."),
    ("p", "<i>Detetar</i> encontra o primeiro e o último som e propõe um bom ponto de Mix. A barra de espaços toca e pausa na "
          "saída de pré-escuta, as setas avançam um segundo, ou uma décima com Shift, e a roda do rato faz zoom."),
    ("h2", "Transições"),
    ("p", "Uma transição é a forma como uma música passa para a seguinte. Abra-a com o botão direito num item da playlist e "
          "<i>Transição para o seguinte…</i> (Ctrl/Cmd+G), ou com <b>Transição…</b> nas Playlists diárias."),
    ("fig", "segue", "Transição para o seguinte…", [
        ("Join", "Em cima, o fim da primeira música; em baixo, o início da seguinte, unidas no zero. <b>Arraste</b> qualquer uma para mover a transição. A linha amarela é o nível da primeira, ou seja, o seu fade."),
        ("MixText", "O <b>Mix</b> é onde começa a música seguinte. Se o mover, cria um marcador Mix e a primeira música continua no nível máximo até ao seu Cue Out. <i>Automático</i> volta ao crossfade habitual."),
        ("OutText", "O <b>Cue Out</b> é onde a primeira música para."),
        ("InText", "O <b>Cue In</b> é onde a música seguinte começa dentro do seu ficheiro, nunca depois da Intro."),
        ("PlayButton", "<b>Ouvir a transição</b> toca-a desde uns segundos antes, tal como vai sair no ar, na pré-escuta. Também com a barra de espaços. Se mudar algo enquanto toca, ouve logo o resultado."),
        ("SummaryText", "O que vai acontecer, dito por palavras."),
    ]),
    ("p", "A transição fica guardada nos pontos de cue das duas músicas, por isso vale onde quer que elas toquem e em todos os estúdios que partilham a biblioteca."),
    ("h2", "Etiquetas"),
    ("fig", "tag-editor", "Editar etiquetas…", []),
    ("p", "Aqui muda o título, o artista, o álbum, o género, o ano, o número da faixa, o comentário e a capa, que ficam gravados no próprio ficheiro. Um ficheiro que está no ar só pode ser alterado depois de acabar."),
    ("h2", "Relatórios"),
    ("fig", "reports-adverts", "Relatórios", [
        ("KindBox", "Escolha o relatório: <b>Música emitida</b> para os direitos de autor, <b>Totais de música por canção</b>, <b>Anúncios</b> para o comprovativo de emissão, <b>Tudo</b>, o registo de emissão completo (com os ouvintes quando cada item começou, se forem contados), ou <b>Ouvintes por hora</b>."),
        ("AdvertiserBox", "Para o comprovativo de emissão, um anunciante ou todos."),
        ("HtmlButton", "<b>Guardar como página para imprimir…</b> serve para imprimir ou guardar em PDF. Ao lado, <b>Guardar como CSV…</b> dá uma folha de cálculo."),
    ]),
    ("h2", "GPIO: os faders e as luzes da mesa"),
    ("p", "Com GPIO, a mesa de mistura comanda o programa: subir o fader de um leitor põe-no a tocar, e as luzes da mesa "
          "acendem-se com o que está no ar. O Sintoniza fala com a mesa de três maneiras, que podem ser usadas ao mesmo tempo: "
          "por uma porta série, pela rede ou por MIDI. Tudo isto está em Definições → <b>GPIO</b>."),
    ("p", "<b>Porta série.</b> Usa as linhas de controlo de uma porta série, e um adaptador USB serve. As entradas são "
          "<b>CTS</b> no pino 8, <b>DSR</b> no 6 e <b>DCD</b> no 1. Cada uma é um contacto entre essa linha e DTR, no pino 4, "
          "ou RTS, no pino 7."),
    ("p", "Para cada entrada escolhe o que acontece quando o contacto <b>fecha</b> e quando <b>abre</b>: Tocar, que é o "
          "arranque pelo fader; Seguinte; Parar; Pausa / continuar; um cart da cartwall no ecrã; <b>Fader do micro em cima "
          "(MIC LIVE)</b>; <b>Fader do micro em baixo</b>; ou nada. Com as duas ações do micro, o fader do microfone da mesa "
          "acende e apaga o sinal MIC LIVE no ecrã."),
    ("p", "As saídas <b>DTR</b> e <b>RTS</b> ficam ligadas enquanto algo toca, enquanto um anúncio está no ar, enquanto uma "
          "entrada em direto ou um stream está no ar, quando há silêncio, ou com <b>O sinal NO AR (micro aberto, programa ou "
          "entrada em direto)</b>, que serve para acender uma lâmpada verdadeira à porta do estúdio. Uma saída que não use fica "
          "sempre ligada, para alimentar os contactos das entradas."),
    ("tip", "O arranque pelo fader mais habitual é este: quando CTS fecha, Tocar; quando abre, com o fader em baixo, Seguinte ou nada. "
            "Um contacto que oscila conta só uma vez, e Seguinte, Parar e Pausa não atuam com um anúncio no ar. "
            "<b>Testar as entradas</b> mostra-as a mudar enquanto mexe nos faders."),
    ("p", "<b>Rede.</b> Serve para uma caixa de GPIO em rede ou para uma mesa que envia textos, numa porta TCP ou UDP. Cada "
          "<b>texto</b> recebido faz uma coisa, por exemplo FADER1_ON para Tocar. O programa também <b>envia</b> textos quando "
          "um sinal começa e acaba, como ONAIR_ON e ONAIR_OFF, para todas as ligações TCP e, se quiser, por UDP. Em <b>Só de</b> "
          "ponha os endereços autorizados, porque quem chegar a essa porta pode carregar em Play."),
    ("p", "<b>MIDI</b>, em Linux e Windows, serve para uma mesa digital ou um controlador. Cada <b>nota</b> ou <b>controlador</b>, "
          "com o seu canal e número, faz uma coisa ao <b>carregar</b> e outra ao <b>largar</b>. Um fader conta uma vez em cada "
          "sentido, ao passar o meio. As <b>luzes</b> são notas enviadas ligadas e desligadas."),
    ("p", "Na rede e no MIDI, <b>Ouvir</b> mostra o que chega enquanto carrega num botão ou mexe num fader, e <b>Juntar o "
          "último recebido</b> põe-no na lista. Assim não precisa de escrever nada."),
    ("h2", "Controlo remoto web"),
    ("p", "O controlo remoto permite comandar a emissão a partir de um telemóvel ou de outro computador na rede da estação. "
          "É uma página web, sem nada para instalar."),
    ("fig", "settings-remote", "Definições → Remoto", [
        ("RemoteBox", "Ligue-o aqui. Vem desligado."),
        ("RemotePortBox", "A porta, 8090 por omissão. A página pede um utilizador da estação com a permissão <b>Controlo remoto web</b>, como o <b>dj</b> (capítulo 4, Utilizadores)."),
        ("RemoteAddressText", "O endereço a abrir no telemóvel, ligado à mesma rede."),
    ]),
    ("p", "A página está arrumada como o ecrã de emissão: o leitor com a forma de onda, o painel da estação com o relógio e os níveis, a playlist, e a cartwall e a biblioteca."),
    ("fig", "remote-desktop", "O controlo remoto num computador", []),
    ("list", [
        "Vê o que está no ar com a forma de onda, o tempo decorrido e o que falta, as contagens para falar e para o bloco, o stream e o silêncio, o relógio, a hora certa, o fim da playlist, o próximo evento e os níveis.",
        "<b>▶ ❚❚ ⏭ ■</b> e <b>AUTO</b> funcionam como no ecrã. Um anúncio no ar só é cortado depois de confirmar.",
        "Na <b>playlist</b>, com as mesmas cores, toque num item para <i>▶ Já</i>, que o põe no ar, ou <b>✕</b>, que o tira.",
        "Em <b>CARTWALL</b>, um toque dispara o cart, e a lista ao lado dos separadores muda de cartwall. Em <b>BIBLIOTECA</b>, procure uma música e junte-a a seguir ao item no ar, ou registe um <b>pedido</b> com ♥.",
    ]),
    ("fig", "remote-phone", "O controlo remoto num telemóvel: o mesmo, com um painel debaixo do outro", []),
    ("warn", "A página usa HTTP simples e é para a rede da própria estação. De fora, use-a só através da VPN da estação e nunca abra a porta no router. Cinco palavras-passe erradas bloqueiam a página durante um minuto."),
    ("h2", "Vários computadores: um no ar, os outros de reserva"),
    ("p", "Com uma biblioteca MySQL partilhada, cada computador com o Sintoniza Emi da estação conhece os outros. <b>Um está "
          "no ar</b> e os restantes ficam <b>de reserva</b>, prontos e calados: sem playlist, sem sinal horário, sem stream e "
          "sem nada no histórico. O primeiro a arrancar vai para o ar."),
    ("p", "No computador que está no ar, a luz <b>ESTÚDIO</b> mostra quais estão de reserva. A barra de estado da Sintoniza Pro mostra o mesmo. Um computador de reserva que vai para o ar no lugar de outro liga o stream se esse o tinha ligado."),
    ("fig", "main-standby", "Um computador de reserva", [
        ("StandbyBanner", "A barra <b>RESERVA</b> aparece no topo quando outro computador está no ar."),
        ("StandbyText", "A primeira linha diz que computador está no ar e o que está a tocar. A segunda diz o que este fará: <i>Este computador vai para o ar automaticamente se o principal deixar de responder por 15 s</i>, ou que só vai para o ar quando carregar em Ir para o ar."),
        ("TakeOnAirButton", "<b>Ir para o ar</b>, ou Play, põe este computador no ar depois de confirmar. O outro para e fica de reserva. Em AUTO, a hora atual começa a partir do item previsto para este momento."),
    ], True),
    ("fig", "settings-studios", "Definições → Segurança → Vários computadores no ar", [
        ("TakeOverBox", "<b>Sozinho</b>: se o computador no ar deixar de responder durante este tempo, porque falhou, foi desligado ou perdeu a rede, este vai para o ar."),
        ("TakeOverSecondsBox", "Quantos segundos esperar, 15 de início. Desligue esta opção num computador que nunca deve ficar com o ar, como um computador de produção com o Emi aberto."),
    ], True),
    ("warn", "Um computador que perde a rede continua a tocar, porque não sabe que outro ficou com o ar. Até a rede voltar, "
             "tocam os dois; depois, ele vê o outro no ar e passa a reserva. Num computador de reserva os carts continuam a "
             "tocar, mas não entram no histórico. Todos os estúdios têm de ter a mesma versão: o primeiro a arrancar uma versão "
             "nova atualiza a base de dados, e uma versão mais antiga deixa de a conseguir abrir."),
    ("h2", "O microfone numa rádio web"),
    ("p", "Numa rádio web o microfone passa pelo computador. Com <i>Rádio web</i> escolhido em Pro → Definições da estação, "
          "aparece o botão <b>MIC</b>, também na tecla F9. Enquanto o microfone está aberto, a música e os carts baixam."),
    ("p", "Em Definições → Microfone escolhe se o botão funciona enquanto o mantém premido ou se carrega uma vez para abrir e "
          "outra para fechar. Numa rádio FM o microfone passa pela mesa e este botão não aparece."),
    ("h2", "Todas as definições"),
    ("p", "As definições deste computador ficam nas <b>Definições</b> do programa de emissão:"),
    ("table", ["Separador", "Para que serve"], [
        ["Geral", "O idioma dos ecrãs e mais idiomas a partir da pasta das traduções"],
        ["Áudio", "As saídas da Música, do Cartwall e da Pré-escuta"],
        ["Microfone", "Numa rádio web: a entrada, os níveis e quanto baixa a música"],
        ["Segurança", "O arranque e as falhas, vários computadores no ar, o registo e as cópias de segurança"],
        ["Biblioteca", "A base de dados, as análises automáticas e os caminhos deste computador"],
        ["Atalhos", "As teclas de Play, Pausa, Seguinte, Parar, MIC, do stream e de cada cart"],
        ["RDS", "Se este computador envia para o codificador RDS, e como lá chega"],
        ["Playlist", "Quanto da hora seguinte a playlist mostra"],
        ["Ecrã", "As luzes de estado, os contadores grandes e o sinal NO AR"],
        ["GPIO", "Os faders e as luzes da mesa, por porta série, rede ou MIDI"],
        ["Remoto", "O controlo remoto web: ligar e desligar, a porta e o endereço"],
        ["Acerca de", "A versão, as atualizações e este manual"],
    ]),
    ("p", "As da estação ficam na <b>Sintoniza Pro → Definições da estação</b>:"),
    ("table", ["Separador", "Para que serve"], [
        ["Estação", "Nome, logótipo, género, site, rádio FM ou web, símbolo da moeda"],
        ["Áudio", "Os crossfades e o nivelamento de volume"],
        ["Sinal horário", "Os bips ou um ficheiro à hora certa, e as horas faladas, como HRS05.mp3 às 05:00"],
        ["Takes", "A pasta dos takes da hora e da temperatura, e onde fica a estação"],
        ["Segurança", "A deteção de silêncio e a música de enchimento"],
        ["Biblioteca", "Outras pastas de música, para músicas sem categoria"],
        ["Streaming", "O servidor Icecast ou Shoutcast, o formato e o bitrate, e a contagem de ouvintes"],
        ["RDS", "O PS, o radiotexto e o RT+"],
        ["Site", "O que está a tocar, enviado para o site da estação"],
        ["Alertas", "Avisos por Telegram, e-mail ou web: silêncio, stream em baixo, falhas, disco cheio, relógio, cópias, playlists de amanhã por preparar"],
        ["Playlist", "Quantos dias de playlists diárias se fazem com antecedência e as cores das linhas"],
    ]),
]),

("Sintoniza Pro", "schedule", [
    ("p", "A Sintoniza Pro é o programa de produção da estação. Neste capítulo vê como se gerem as músicas, as categorias e "
          "as cartwalls, os utilizadores, as quotas da ERC, o site, os eventos, os programas em direto, a publicidade, os "
          "modelos de hora, as playlists diárias e os voice tracks."),
    ("p", "O programa abre no <b>Início</b>, que mostra a estação num relance."),
    ("fig", "production", "Sintoniza Pro: Início", [
        ("MainMenu", "O <b>menu</b>: Ficheiro, Biblioteca, Programação, Publicidade, Estação e Ajuda."),
        ("SaveButton", "A <b>barra de ferramentas</b>, com Guardar (Ctrl+S) e as ferramentas mais usadas."),
        ("NavPanel", "As <b>ferramentas</b>, por grupos: Início; Biblioteca, com músicas, categorias e importação; Programação, com modelos de hora, playlists diárias, eventos e programas; Publicidade, com blocos, clientes e campanhas; Estação, com definições, cartwalls e utilizadores."),
        ("HomeClock", "O relógio e a data."),
        ("HomeToday", "<b>Hoje</b>: as horas planeadas, os blocos e anúncios marcados, os eventos e os voice tracks."),
        ("HomeWarnings", "<b>A verificar</b>: ficheiros em falta, músicas sem categoria, horas sem modelo, campanhas a acabar esta semana, blocos que ficam longos demais, alterações por guardar e o que não está pronto nas <b>playlists diárias de amanhã</b> (veja abaixo)."),
        ("HomeComputer", "<b>Este computador</b>: o sistema, o processador, a memória, o disco livre e há quanto tempo está ligado. Ao lado, a biblioteca e a base de dados."),
        ("NowTitleText", "O que está <b>a tocar</b> no ar, segundo o histórico de todos os estúdios, e o modelo desta hora."),
        ("BoxStationText", "A estação, a base de dados e quem está a trabalhar aqui."),
        ("StatusVersion", "A <b>barra de estado</b>: a versão, a estação, a base de dados, o utilizador e as alterações por guardar…"),
        ("StatusClock", "…e a data e a hora."),
    ]),
    ("p", "Com uma biblioteca partilhada, a barra de estado também diz que computador está no ar e quais estão de reserva (capítulo 3)."),
    ("p", "As <b>playlists diárias de amanhã</b> são verificadas a cada dez minutos, para um problema aparecer na véspera e "
          "não no ar. O Início diz-lhe, numa linha cada, se há uma hora com modelo mas ainda sem playlist diária, uma hora que "
          "ficou vazia porque o modelo não encontrou música, uma hora com mais de três minutos a menos (a música de enchimento "
          "tocaria o resto), uma música cujo ficheiro não está lá, e um bloco publicitário ou evento que ia deixar algo de fora, "
          "como um anúncio ou o ficheiro das notícias que não se encontra. Uma pasta de noticiários que recebe um ficheiro novo "
          "a cada hora não conta: até lá terá um. O computador no ar também pode enviar isto como alerta, a partir das 18:00 "
          "ou da hora que escolher em <b>Definições da estação → Alertas</b>, para alguém o resolver antes de a noite acabar. "
          "Os alertas têm de estar ligados aí, com Telegram, e-mail ou um endereço web."),
    ("tip", "Se o Início disser que nenhum dos ficheiros de amanhã se alcança deste computador, ou a partilha da música está em "
            "baixo, ou este computador do escritório vê a música noutro sítio que os estúdios. Diga onde ela está em "
            "<b>Definições → Biblioteca → Caminhos deste computador</b>, no programa de emissão. A partir daí os ficheiros "
            "também são verificados daqui."),
    ("h2", "Músicas"),
    ("p", "Aqui está a biblioteca inteira, com pesquisa, categorias, andamento e energia, e etiquetas. Com o botão direito "
          "numa música chega à categoria, às restrições, aos dados da ERC, a <b>Fora do site</b>, a <b>Editar etiquetas…</b> "
          "e a <b>Editar pontos de cue…</b>."),
    ("fig", "pro-songs", "Músicas: a ouvir uma música", [
        ("SearchBox", "Pesquise por título, artista ou álbum…"),
        ("CategoryBox", "…dentro de uma categoria e das que estão dentro dela."),
        ("TrackList", "Para ouvir uma música, faça <b>duplo clique</b> nela, ou selecione-a e carregue na barra de espaços."),
        ("ListenButton", "▶ / ❚❚ e ■ tocam, pausam e param."),
        ("ListenSlider", "Arraste para saltar para outro ponto da música."),
        ("ListenTitle", "O que está a tocar. Ouve-se na saída de <b>Pré-escuta</b> deste computador, definida em Definições → Áudio, e nunca no ar."),
    ]),
    ("tip", "<b>Editar pontos de cue…</b> abre o mesmo editor do programa de emissão. O que guardar fica na biblioteca para todos os estúdios, a emissão passa a usá-lo no espaço de um minuto e as playlists diárias contam já com a nova duração."),
    ("h2", "Categorias"),
    ("p", "As categorias arrumam a biblioteca e são a base dos modelos de hora. Todas as estações têm as mesmas oito "
          "categorias no topo, que não se podem renomear, mudar de sítio nem apagar: <b>Music</b>; <b>Jingles</b>; "
          "<b>Promos</b>; <b>ProgAutor</b>, para programas gravados fora; <b>Fillers</b>; <b>Redação</b>, para o noticiário; "
          "<b>Spots</b>, para os anúncios; e <b>Takes</b>, as inserções de voz com a hora ou a temperatura, como se houvesse "
          "um locutor."),
    ("p", "As suas categorias ficam debaixo destas, com <b>Nova dentro desta</b>, ou ao lado delas no topo, com <b>Nova no "
          "topo</b>. Essas pode renomear, mudar de sítio e apagar à vontade."),
    ("fig", "categories-fixed", "Categorias: uma das oito da estação", [
        ("CategoryList", "As categorias, em árvore. O cadeado 🔒 marca as oito da estação."),
        ("FixedPanel", "Para que serve a categoria. Nestas só se mudam a cor e as pastas."),
        ("AddChildButton", "<b>Nova dentro desta</b> cria uma categoria sua debaixo da selecionada, como Music › Anos 80 ou Spots › Clientes."),
    ]),
    ("fig", "categories", "Categorias: uma categoria sua", [
        ("ParentBox", "<b>Dentro de</b> muda-a para dentro de outra categoria."),
        ("Swatches", "A cor com que aparece nas listas."),
        ("FolderList", "As suas <b>pastas</b>. As músicas que lá estão pertencem a esta categoria, e as que lá forem postas depois também."),
        ("ApplyButton", "<b>Guardar alterações</b>."),
    ]),
    ("h2", "Cartwalls"),
    ("fig", "cartwalls", "Cartwalls", [
        ("PageList", "As cartwalls, com <b>Nova</b>, <b>Copiar</b>, <b>Mudar o nome…</b> e <b>Apagar</b>."),
        ("CartGrid", "Os 12 carts, tal como o estúdio os vê. Clique num para o alterar, ou largue um ficheiro em cima dele."),
        ("ChooseFileButton", "O som do cart: <b>Escolher ficheiro…</b>, <b>Ouvir</b> na pré-escuta e <b>Limpar</b>."),
        ("ColorPanel", "A cor do cart…"),
        ("IconPanel", "…e o ícone."),
        ("SaveButton", "<b>Guardar</b>. Todos os estúdios recebem as cartwalls no espaço de um minuto."),
    ]),
    ("tip", "Tal como para a música, use ficheiros que todos os estúdios consigam alcançar, numa pasta partilhada."),
    ("h2", "O que está a tocar no site"),
    ("p", "Os ouvintes gostam de ver no site a música que está a tocar, com a capa. Em <b>Definições da estação → Site</b>, "
          "o Sintoniza envia essa informação sempre que muda o que está no ar."),
    ("p", "Há duas maneiras de o fazer, que podem ser usadas juntas. A primeira é um envio em JSON para um endereço do site, "
          "protegido por uma chave. A segunda são ficheiros feitos a partir de modelos, gravados numa pasta ou enviados por "
          "FTP, que servem também para um codificador de streaming ou para um site que só aceita ficheiros."),
    ("fig", "station-website", "Definições da estação → Site", [
        ("WebUrlBox", "O <b>endereço</b> para onde é enviado o JSON. Se só quiser os ficheiros mais abaixo, deixe-o vazio. Por baixo fica a <b>chave</b>: a página só aceita envios que a tragam. Com <b>Só músicas</b>, durante jingles, anúncios e programas o site continua a mostrar a última música."),
        ("WebPostTestButton", "<b>Testar</b> envia uma vez para esta página, com a chave, e diz o que ela respondeu: <i>A página aceitou</i>, ou porque não (uma chave errada dá 403)."),
        ("WebCoverBox", "Envia também a <b>capa</b> da música, quando o ficheiro a tem."),
        ("WebDelayBox", "<b>Atrasar cada mudança</b> estes segundos. Ponha aqui o atraso do stream, para o site mudar no momento em que os ouvintes ouvem a música, e não antes."),
        ("WebOutputsPanel", "<b>FICHEIROS E FTP</b>: cada linha é um ficheiro a escrever. O destino pode ser um ficheiro neste computador ou numa partilha, um endereço ftp:// ou ftps://, ou um endereço http(s):// que recebe o ficheiro por HTTP PUT."),
        ("AddWebTextButton", "<b>Juntar um texto</b>: uma linha simples, ou uma página XML ou HTML, feita a partir de um modelo."),
        ("AddWebCoverButton", "<b>Juntar a capa</b>: a imagem da capa da música, ou o logótipo da estação quando a música não tem capa."),
    ]),
    ("p", "Num modelo de texto escreve o que quer e põe campos entre chavetas, que são substituídos na hora: {artist} e "
          "{title} para o que está no ar, {next_artist} e {next_title} para o que vem a seguir, {station} para o nome da "
          "estação, {show} e {presenter} durante um programa em direto, e ainda {type}, {duration} e {started}. Por exemplo, "
          "<i>Agora: {artist} - {title}</i>. Quando não há nada no ar, {title} dá o nome da estação. Num ficheiro .xml, .html "
          "ou .json, os campos são escritos de forma segura para esse formato."),
    ("p", "Num destino FTP, preencha o <b>Utilizador FTP</b> e a palavra-passe. Num endereço web, a caixa da palavra-passe "
          "leva a chave da página."),
    ("p", "Cada texto e a capa têm o seu botão <b>Testar</b>, no topo da caixa. Escreve esse destino uma vez, no ficheiro, no "
          "servidor FTP ou na página PHP, e diz por baixo se correu bem ou o que falhou. O teste envia o que está no ar, com as "
          "mesmas regras do ar: com <b>Só músicas</b> não envia um jingle nem um anúncio, nem uma música marcada <i>Fora do "
          "site</i>. Sem nada no ar, um texto leva o nome da estação, nunca palavras de teste inventadas no seu site. O teste "
          "da capa envia a capa da música no ar, senão a imagem para músicas sem capa; sem nenhuma, diz que nada foi escrito, "
          "como no ar. Os testes funcionam também no Sintoniza Pro, para poder configurar tudo a partir do escritório. A partir "
          "da Pro não se sabe o que vem a seguir nem o programa em direto; o estúdio envia-os com a próxima música. No estúdio, o programa de "
          "emissão tem ainda <b>Enviar agora</b>, que envia para todos de uma vez."),
    ("p", "O projeto inclui uma página PHP de exemplo, na pasta docs/now-playing, que recebe o JSON e o guarda em "
          "nowplaying.txt e nowplaying.json para o site mostrar. A mesma página aceita os ficheiros enviados por HTTP PUT, "
          "como a capa."),
    ("tip", "Há músicas que não quer anunciar no site, como uma faixa de fundo ou uma versão sem direitos para a internet. Na "
            "biblioteca, clique nelas com o botão direito e escolha <b>Fora do site</b>. Essas músicas nunca são enviadas, e "
            "o site continua a mostrar o que tinha antes."),
    ("h2", "Várias estações numa biblioteca"),
    ("p", "Uma biblioteca pode ter várias estações, por exemplo uma rádio de música e uma de informação do mesmo grupo. "
          "Partilham a música e os utilizadores. Cada uma tem a sua programação, playlists diárias, definições, cartwalls e histórico."),
    ("list", [
        "<b>Estação → Nova estação…</b> no Sintoniza Pro cria uma. Começa com uma cópia das definições da estação atual e uma programação vazia. O servidor de stream, o nome RDS e o envio para o site começam em branco, para nunca sair para o ar como a outra estação.",
        "Com várias estações, a entrada no Sintoniza Pro tem uma lista <b>Estação</b>: escolha ao entrar aquela em que vai trabalhar. <b>Estação → Trabalhar em</b> muda depois. O Pro abre de novo nela e continua com a sessão iniciada. O programa de emissão do estúdio no mesmo computador fica com a sua estação.",
        "Em cada estúdio, <b>Definições → Geral → Estação</b> escolhe a estação que ele põe no ar. Só aparece quando a biblioteca tem duas estações, e vale a partir da próxima vez que o Sintoniza Emi abrir.",
        "Em <b>Utilizadores</b>, um utilizador pode ficar limitado a algumas estações. Entrar noutra estação é então recusado.",
        "Um administrador de <b>Todas as estações</b> gere todos os utilizadores. Um administrador limitado a uma estação, o administrador dessa rádio, pode fazer tudo nela e nada com a programação, as definições ou os utilizadores das outras. A biblioteca de música, as categorias e a importação são de todas as estações, por isso o que muda aí muda para todas. Em Utilizadores só vê e gere os utilizadores dessa estação, e quem acrescentar fica para ela. Um utilizador que trabalha também noutra estação, ou em todas, não é seu para alterar, e ninguém muda as suas próprias estações.",
    ]),
    ("h2", "Utilizadores e permissões"),
    ("p", "O programa de emissão, a Sintoniza Pro, o Sintoniza Import, as Definições do programa de emissão e o controlo remoto "
          "web pedem um <b>utilizador e uma palavra-passe</b>. São os utilizadores da estação, os mesmos em todos os estúdios."),
    ("p", "Uma biblioteca nova traz dois: <b>admin</b>, com a palavra-passe <b>admin</b>, que pode fazer tudo, e <b>dj</b>, com "
          "a palavra-passe <b>dj</b>, que é um locutor: trabalha no ar, grava voice tracks e usa o controlo remoto web. "
          "<b>Mude primeiro essas palavras-passe.</b> Até o fazer, o Início da Pro vai lembrá-lo."),
    ("fig", "login", "Entrar", [
        ("UserBox", "O utilizador. O programa lembra-se do último…"),
        ("PasswordBox", "…e a palavra-passe."),
        ("LoginButton", "<b>Entrar</b>. Na Pro, Ficheiro → <b>Mudar de utilizador…</b> troca de utilizador e Ficheiro → <b>Sair</b> volta a cobrir a Pro com o quadro de entrada."),
    ]),
    ("fig", "users", "Estação → Utilizadores", [
        ("UserList", "Os utilizadores da estação."),
        ("AddButton", "<b>Novo utilizador</b>, e <b>Apagar</b> ao lado."),
        ("NameBox", "O nome…"),
        ("PasswordBox", "…e uma nova palavra-passe. Se a deixar vazia, fica a que estava."),
        ("EnabledBox", "<b>Pode entrar</b>. Desmarque para impedir alguém de entrar sem o apagar."),
        ("AdminPresetButton", "<b>Tudo</b> e <b>Locutor</b> marcam de uma vez os conjuntos de permissões habituais."),
        ("PermissionPanel", "O que esta pessoa pode usar. A Pro só lhe mostra essas ferramentas, menus e botões."),
        ("SaveUserButton", "<b>Guardar utilizador</b>."),
    ]),
    ("table", ["Permissão", "O que deixa fazer"], [
        ["No ar", "Trabalhar no programa de emissão: tocar, a playlist, a cartwall, o microfone"],
        ["Biblioteca", "Músicas, categorias e importação de música, incluindo o Sintoniza Import"],
        ["Programação", "Modelos de hora, playlists diárias, eventos e programas"],
        ["Voice tracks", "Gravar voice tracks. Vê as playlists diárias, mas não muda as músicas"],
        ["Publicidade", "Blocos publicitários, clientes e campanhas"],
        ["Estação", "As definições e as cartwalls da estação"],
        ["Utilizadores", "Criar e alterar utilizadores"],
        ["Definições deste computador", "Abrir as Definições do programa de emissão: placas de som, base de dados, teclas"],
        ["Controlo remoto web", "Comandar a emissão a partir de um telemóvel"],
        ["Stream", "Ligar e desligar o stream da internet à mão (o botão STREAM, o seu menu ou a sua tecla). Sem ela, um locutor não desliga o stream por engano; ele continua a ligar-se sozinho quando deve."],
    ]),
    ("tip", "As palavras-passe só ficam guardadas como um hash seguro. Se alguém se esquecer da sua, outro utilizador com a permissão <b>Utilizadores</b> define-lhe uma nova. Ninguém se pode apagar nem bloquear a si próprio, nem tirar a si mesmo a permissão <b>Utilizadores</b>."),
    ("h2", "Quotas ERC (música portuguesa)"),
    ("p", "A Lei da Rádio, nos artigos 41.º a 47.º na redação de 2024, pede que, das músicas emitidas num mês, pelo menos "
          "<b>30 %</b> sejam música portuguesa. Dessa música portuguesa, pelo menos <b>60 %</b> tem de ser em língua "
          "portuguesa interpretada por cidadãos da UE, e pelo menos <b>35 %</b> editada pela primeira vez nos últimos 12 meses."),
    ("p", "As quotas contam no mês inteiro e também, à parte, <b>de segunda a sexta, das 7:00 às 20:00</b>. Todos os meses a "
          "estação envia à ERC, pelo portal, o que emitiu."),
    ("p", "Para isso, cada música precisa de quatro dados: se é música portuguesa, a língua em que é cantada, o país do "
          "intérprete e a data da primeira edição. Pode dá-los a uma categoria inteira em <b>Categorias</b>, e as músicas e "
          "categorias dentro dela ficam com eles. Ou pode dá-los música a música em <b>Músicas</b>, com o botão direito e "
          "<b>Classificação ERC…</b>."),
    ("fig", "erc-song", "Classificação ERC de uma música", [
        ("PortugueseBox", "<b>Música portuguesa</b>: sim, não, ou o que disser a categoria."),
        ("LanguageBox", "A <b>língua</b> em que é cantada, como pt-PT, pt-BR ou en."),
        ("CountryBox", "O <b>país do intérprete</b>, da lista da estação com os códigos da ERC."),
        ("ReleaseBox", "A <b>primeira edição</b>, como aaaa-mm-dd, aaaa-mm ou só o ano. Se ficar vazia, conta o ano da etiqueta."),
    ]),
    ("fig", "erc", "Programação → Quotas ERC", [
        ("FromBox", "O período: <b>Mês passado</b>, <b>Este mês</b> ou as datas que quiser…"),
        ("CheckButton", "…e depois <b>Verificar</b>."),
        ("AllPanel", "As três quotas no período inteiro: verde quando cumpridas, vermelho quando não, com quantas músicas de quantas."),
        ("DaytimePanel", "O mesmo, de segunda a sexta das 7:00 às 20:00. Enquanto o período não acabar, um terceiro cartão junta as playlists diárias ainda por emitir."),
        ("MissingList", "<b>Músicas por classificar</b>: emitidas ou planeadas sem os dados da ERC. Até serem classificadas, contam como não portuguesas."),
        ("ClassifyButton", "<b>Classificar…</b> as selecionadas, ou <b>🔎 Procurar no MusicBrainz…</b>, que vai buscar à internet o país do intérprete, a língua e a primeira edição. Desmarque o que não estiver certo e guarde o resto."),
        ("AddToLibraryButton", "Músicas que tocaram a partir de <b>ficheiros fora da biblioteca</b>: <b>Juntar à biblioteca, em</b> uma categoria, para as poder classificar."),
        ("ExportButton", "<b>Exportar para a ERC (XML)…</b> cria o ficheiro que o portal da ERC aceita, com uma PLAYLIST por dia."),
        ("AliasBox", "O nome do serviço de programas na ERC, o ALIAS."),
        ("CountryPanel", "Os países e os seus códigos na ERC, e quais são da UE. Portugal é 045; os outros copiam-se do portal."),
    ]),
    ("tip", "As playlists diárias só conseguem cumprir as quotas se tiverem <b>músicas classificadas</b> por onde escolher. Em <b>Músicas</b>, selecione as músicas de uma categoria, carregue no botão direito e escolha <b>ERC: procurar no MusicBrainz…</b>. Conte com cerca de 3 segundos por música."),
    ("p", "Com <b>Planear para as quotas</b>, à direita e ligado por omissão, cada hora das playlists diárias é feita para "
          "cumprir as três quotas por si só. Entra uma música portuguesa mais ou menos uma vez em cada três quando há poucas, "
          "e entre elas dá-se preferência às cantadas em português por intérpretes da UE e às recentes, onde fazem falta. As "
          "escolhas saem das músicas classificadas da categoria de cada linha, dentro das regras de descanso."),
    ("h2", "Ouvintes"),
    ("p", "Quantas pessoas estão a ouvir o stream? O Sintoniza pode contá-las. Ligue <b>Contar os ouvintes</b> em "
          "<b>Definições da estação → Streaming</b>. A cada minuto, o computador no ar pergunta ao servidor Icecast ou Shoutcast "
          "quantos estão a ouvir e guarda o número na base de dados. Bastam as estatísticas públicas do servidor, sem palavra-passe."),
    ("p", "Se o stream sai de outro codificador e não do Sintoniza, escreva o endereço do servidor em <b>Estatísticas de</b>, "
          "por exemplo <code>http://stream.example.com:8000</code>. Com mais de um ponto de montagem, um stream MP3 e outro AAC, "
          "por exemplo, escreva-os todos: <code>/live, /live.aac</code>. São somados, e <code>*</code> conta todos os do servidor."),
    ("p", "No estúdio, a luz <b>OUVINTES</b> na barra de estado mostra o número de agora. No Sintoniza Pro, <b>Estação → "
          "Ouvintes</b> mostra um dia hora a hora: cada barra é a média da hora, e com o rato por cima vê o máximo e o mínimo. "
          "Clique numa hora para ver o que passou nela e quantos estavam a ouvir quando cada música começou. Assim percebe que "
          "programas e que músicas seguram os ouvintes. A página Início também mostra os ouvintes de agora."),
    ("tip", "Um computador de reserva não conta, para os mesmos ouvintes nunca serem contados duas vezes. Se o computador no ar "
            "parar, o que fica no ar passa a contar."),
    ("h2", "Restrições das músicas"),
    ("p", "As restrições dizem quando uma música pode ser escolhida automaticamente. Uma canção de Natal só em dezembro, uma "
          "música mais pesada só à noite. Abra-as em <b>Músicas</b>, com o botão direito e <b>Restrições…</b>."),
    ("p", "As playlists diárias cumprem-nas sempre, e a música de enchimento respeita as datas, os dias e as horas. Se a "
          "puser à mão, a música toca na mesma. As músicas com restrições aparecem com ⏱ na lista."),
    ("fig", "restrictions", "Restrições de uma música", [
        ("FromBox", "<b>Só de</b>…"),
        ("UntilBox", "…<b>até</b>. Vazio quer dizer sem limite."),
        ("DayGrid", "Nestes <b>dias</b>…"),
        ("HourGrid", "…e a estas <b>horas</b>. Aceso quer dizer que pode tocar."),
        ("DayHoursButton", "Escolhas rápidas: todas, 7:00 a 20:00, 20:00 a 7:00."),
        ("MaxDayBox", "No máximo tantas vezes por <b>dia</b>…"),
        ("MaxWeekBox", "…e por <b>semana</b>, de segunda a domingo. Com 0 não há limite."),
        ("RemoveButton", "<b>Sem restrições</b>."),
    ]),
    ("h2", "Takes: a hora e a temperatura"),
    ("p", "Os takes são pequenas gravações de voz que o programa monta <b>no momento em que vão para o ar</b>, por isso dizem "
          "sempre a hora e a temperatura certas. Para os usar, num modelo de hora escolha <b>Juntar uma linha → 🕒 Hora / "
          "temperatura…</b> e indique a hora, a temperatura ou as duas. Juntar uma categoria ou pasta de takes faz a mesma pergunta."),
    ("fig", "station-takes", "Definições da estação → Takes", [
        ("TakesFolderText", "A <b>pasta</b> dos takes: HRS00 a HRS23 para as horas, MIN01 a MIN59 para os minutos, ou HRS10_15 para uma hora inteira, TMP0 a TMP45 para a temperatura e TMPM1 em diante abaixo de zero."),
        ("TakesFolderNote", "O que o programa encontrou nessa pasta."),
        ("LatitudeBox", "Onde fica a estação, para saber a temperatura. Vem do Open-Meteo, que é gratuito, e é lida a cada 10 minutos…"),
        ("TemperatureButton", "…e <b>Ler a temperatura</b> experimenta já."),
    ]),
    ("tip", "Os ficheiros podem estar em subpastas, como Horas, Minutos e Temperatura. Se gravar o mesmo take várias vezes, como HRS10a e HRS10b, o programa escolhe um ao acaso. A hora dita é o minuto mais próximo do momento em que o take começa. Um take sem os seus ficheiros é saltado, e o registo diz qual faltava."),
    ("h2", "A programação"),
    ("p", "A programação é tudo o que se planeia com antecedência: eventos, programas, blocos publicitários, clientes e "
          "campanhas, modelos de hora e playlists diárias. Escolhe cada ferramenta à esquerda."),
    ("p", "<b>Guardar</b>, na barra de ferramentas ou com Ctrl+S, grava na base de dados, e o programa de emissão recebe as "
          "alterações em poucos segundos. O botão acende-se quando há algo por guardar. Se entretanto outra pessoa tiver "
          "gravado, o programa pergunta antes de substituir o trabalho dela."),
    ("tip", "<b>Os calendários da semana</b> funcionam todos da mesma maneira. <b>Clique numa célula</b> para pôr o item "
            "selecionado nesse dia e hora, e outra vez para o tirar. <b>Arraste</b> para preencher várias células, ou "
            "<b>clique numa hora</b> para todos os dias. <b>Adicionar linha</b> junta uma hora exata, como <code>12:59:55</code>; "
            "<code>*:59:55</code> quer dizer esse minuto e segundo em todas as horas."),
    ("h2", "Eventos"),
    ("p", "Um evento é algo que toca a horas certas: o noticiário das 9, a rubrica do trânsito, a ligação à rede nacional."),
    ("fig", "schedule-events", "Eventos: o que toca a horas certas", [
        ("EventList", "Os eventos, com <b>Novo</b> e <b>Apagar</b> por baixo."),
        ("EventGrid", "A semana, com as horas em que o evento selecionado toca."),
        ("EventAddTimeBox", "Uma hora exata para juntar ao calendário como linha."),
        ("SaveButton", "<b>Guardar</b> grava toda a programação."),
    ]),
    ("p", "Um evento pode tocar um <b>ficheiro</b>; o <b>ficheiro mais recente de uma pasta</b>, como o noticiário que é "
          "descarregado para lá, e que é saltado se tiver mais de N minutos; um <b>stream</b>; ou uma <b>entrada do computador "
          "do estúdio</b> em direto, com <b>Entrada…</b>, durante N minutos."),
    ("p", "Também escolhe como o evento começa:"),
    ("list", [
        "<b>Flexível</b>: espera que acabe o que está no ar. Pode deixá-lo começar uns minutos <b>mais cedo</b> e dar-lhe um atraso <b>máximo</b>: uma janela, como o trânsito entre as 8:05 e as 8:20, que apanha o primeiro fim de música dentro dela. Se nenhuma acabar a tempo, o que está no ar desvanece no fim da janela. Uma janela nunca abre antes do início da sua hora, por isso um bloco das 10:00 fica na hora das 10.",
        "<b>Rígido</b>: começa ao segundo, e o que está no ar desvanece.",
        "<b>Acaba à sua hora</b>: acaba exatamente a essa hora, nunca depois. As músicas antes dele são ajustadas, e o que "
        "estiver no ar desvanece se fosse passar da hora. Um evento que acaba às 10:00 é o último da hora das 09:00. Se o "
        "sinal horário estiver em Parar, o evento acaba quando começam os bips. Um intervalo curto antes dele é preenchido "
        "com a categoria Fillers. As músicas juntadas antes dele que não caibam passam para depois, e uma que já não consiga "
        "começar antes dele aparece como NÃO TOCA.",
    ]),
    ("p", "Por fim, diz o que o locutor pode fazer com o evento na playlist: removê-lo e movê-lo, só movê-lo, ou nada."),
    ("h2", "Séries nos eventos"),
    ("p", "Uma série toca um ficheiro diferente em certos dias, sempre no mesmo lugar da grelha. Pense no horóscopo de cada "
          "manhã, com uma gravação para cada dia da semana, ou numa rubrica com um episódio novo em cada data."),
    ("fig", "schedule-series", "Eventos: uma série", [
        ("EventList", "Escolha o evento, por exemplo o horóscopo das 8:15."),
        ("EventSeriesDays", "Em <b>Uma série: outro ficheiro em certos dias</b>, dê um ficheiro a cada dia da semana, de segunda a domingo. Também pode ser uma pasta ou um stream."),
        ("EventSeriesDates", "As datas: um ficheiro para um dia em particular, como um episódio ou um especial de feriado. As datas que já passaram aparecem a cinzento."),
        ("EventSeriesDateButton", "<b>Juntar uma data</b> acrescenta uma data à lista."),
    ]),
    ("p", "Num dado dia, o programa toca o ficheiro dessa data, se houver. Se não houver, toca o do dia da semana. Se também "
          "não houver, toca o ficheiro normal do evento. Se esse estiver vazio, o evento é saltado nesse dia e o registo diz porquê."),
    ("p", "Uma data deixada vazia quer dizer que nesse dia não toca nada. É assim que se tira o horóscopo do dia de Natal. "
          "De vez em quando, <b>Tirar as datas passadas</b> limpa a lista."),
    ("h2", "Programas em direto"),
    ("p", "Em <b>Programação › Programas</b> marca os programas da estação, como o programa da manhã. Cada um tem um "
          "<b>nome</b>, o <b>locutor</b>, quanto tempo dura a partir de cada início, e <b>notas para o locutor</b> com o "
          "alinhamento, os convidados ou o que referir. Coloca-se na semana como um evento."),
    ("p", "Em <b>No ar</b>, escolha <b>Ao vivo</b> para um programa com locutor no estúdio, ou <b>Automático</b> para um que só "
          "dá nome a uma parte do dia, como a música da noite. Um programa ao vivo pergunta ao locutor com sessão iniciada no "
          "ar se está lá, e passa a ASSISTIDO quando ele diz que sim. Um automático fica em AUTO. Em ambos os casos, o nome do "
          "programa e do locutor vão para o RDS e para o site. Nos modelos do RDS e do site, os campos {show} e {presenter} "
          "trazem-nos. O capítulo 3 explica o que o locutor vê."),
    ("p", "Um programa que começa a cada meia hora e dura uma hora é um só programa longo, não vários. Por cima das definições "
          "do programa, a Pro avisa de um programa que começa de novo antes de acabar, de dois programas no ar à mesma hora e "
          "de um programa ao vivo sem locutor, e diz quantas horas por dia estão no ar os programas ao vivo. Os mesmos avisos "
          "aparecem ao guardar, na página Início e na verificação de amanhã."),
    ("h2", "Blocos publicitários"),
    ("fig", "schedule-breaks", "Blocos publicitários", [
        ("BreakList", "Os blocos."),
        ("BreakGrid", "Onde estão na semana. Aqui, a todas as horas aos 50 minutos."),
        ("PartList", "O <b>alinhamento</b> do bloco: jingles e <b>secções</b>. Os anúncios marcados numa secção tocam nesse ponto."),
    ]),
    ("p", "Um jingle pode ser marcado para <i>tocar só quando a secção a seguir tiver spots</i>, a abrir a secção. Um bloco sem "
          "nada marcado é saltado. Os blocos ficam bloqueados na playlist por omissão."),
    ("p", "<b>Máximo</b> dá ao bloco uma duração máxima em segundos. Se os anúncios marcados para hoje o tornarem mais longo, "
          "o Início avisa, para o departamento comercial poder acertar. No ar nada fica de fora."),
    ("h2", "Clientes e campanhas"),
    ("fig", "schedule-clients", "Clientes", [
        ("ClientList", "Os <b>clientes</b>, com o nome que aparece no comprovativo de emissão."),
        ("CampaignList", "As <b>campanhas</b> de cada cliente: os spots, que vão alternando quando há vários, a secção, a posição, e o primeiro e o último dia."),
        ("SpotGrid", "Os blocos da semana, com quantos anúncios tem cada um e quanto duram. <b>Clique</b> num bloco para marcar lá a campanha, todas as semanas ou só nessa data."),
    ]),
    ("p", "<b>Em todos os blocos</b> marca a campanha em todos os blocos que têm a sua secção. A <b>posição</b> pode ser "
          "automática, de 1 a 5 a contar do início da secção, ou de 6 a 10 no fim."),
    ("p", "Cada cliente pode ter um <b>Setor</b>, como Automóveis ou Bancos. Os anúncios automáticos são arrumados de forma a "
          "que o mesmo cliente, ou dois clientes do mesmo setor, nunca fiquem seguidos se houver maneira de o evitar. Não se "
          "quer o anúncio de um stand logo a seguir ao da concorrência. Nenhum anúncio fica de fora por isso, e o Início avisa "
          "quando não há forma de os separar."),
    ("p", "<b>Certificado…</b>, por baixo dos clientes, compara o que foi marcado para o cliente com o que foi para o ar, spot a "
          "spot, num período e numa campanha ou em todas. Cada spot aparece como <b>No ar</b>, com a hora, <b>NÃO FOI PARA O "
          "AR</b>, <b>No ar, não marcado</b> ou <b>Por emitir</b>. <b>Certificado para imprimir…</b> dá uma página para imprimir "
          "ou guardar em PDF e entregar ao cliente."),
    ("p", "Se o Sintoniza Rec estiver a gravar a emissão, <b>▶ Ouvir</b> toca o anúncio selecionado tal como saiu, e <b>Guardar "
          "o áudio…</b> guarda-o em MP3 para enviar ao cliente. O Início avisa quando algum anúncio marcado para ontem não foi "
          "para o ar."),
    ("p", "<b>Faturação…</b> conta os spots de todos os clientes num mês, por omissão o mês passado, campanha a campanha: "
          "marcados, no ar, não emitidos, no ar sem estarem marcados, e o tempo no ar. Conta da mesma maneira que os "
          "certificados, e pode guardar o resultado em CSV para a contabilidade ou imprimi-lo."),
    ("h2", "Playlists: modelos de hora para os programas"),
    ("p", "Um modelo de hora é a receita de uma hora: que categorias tocam e por que ordem. Faz-se um para cada programa ou tipo "
          "de hora, como as Manhãs ou as Tardes, e coloca-se na semana."),
    ("fig", "schedule-templates", "Modelos de hora", [
        ("TemplateList", "Os modelos, um por programa ou tipo de hora."),
        ("TemplateGrid", "As horas em que cada um toca. Aqui, as Manhãs nos dias úteis, das 7 às 10."),
        ("LineList", "As <b>linhas</b> do modelo, que tocam por ordem e recomeçam do início até a hora ficar cheia."),
        ("LineCategoryBox", "Uma linha pode ser uma música de uma <b>categoria da biblioteca</b>, como Music › Atuais, Gold ou Jingles…"),
        ("TempoMinBox", "Para a linha selecionada, um intervalo de <b>andamento</b> e de <b>energia</b>. Aqui, a primeira música da manhã é animada."),
        ("AddFolderLineButton", "…ou um ficheiro de uma <b>pasta</b>."),
    ]),
    ("h3", "Épocas: uma grelha de verão ou de Natal"),
    ("p", "Muitas rádios mudam a semana no verão ou no Natal. Em vez de mudar os modelos à mão, faça uma vez os modelos da "
          "época e dê-lhes essa época. Clique em <b>Épocas…</b> e junte uma, por exemplo <i>Verão, de 1 de julho a 31 de "
          "agosto</i>. Uma época repete-se todos os anos e pode passar a passagem de ano, por exemplo de 15 de dezembro a "
          "6 de janeiro."),
    ("p", "Depois escolha a época na caixa <b>Época</b> do modelo e ponha-o no calendário como de costume. Nos dias da "
          "época, esse modelo toca às suas horas em vez do modelo de todo o ano que lá estiver. As outras horas ficam com o de "
          "todo o ano. <b>Mostrar a semana</b>, por cima do calendário, mostra a semana tal como vai tocar todo o ano ou numa época."),
    ("tip", "Quando uma época começa ou acaba, as playlists diárias das horas que ela muda são refeitas sozinhas, a não ser "
            "que tenham sido alteradas à mão. Faça os modelos da época uns dias antes e confira-os nas Playlists diárias."),
    ("h2", "Regras musicais"),
    ("p", "As regras musicais ajudam a hora a soar bem: sem baladas umas atrás das outras, sem o mesmo artista duas vezes "
          "seguidas. Primeiro, dê às músicas um <b>andamento</b>, de 1 lento a 5 rápido, e uma <b>energia</b>, de 1 calma a 5 "
          "animada. Na biblioteca, selecione-as e use o botão direito. Depois, as linhas de um modelo podem pedir esses valores."),
    ("fig", "schedule-rules", "As regras musicais de um modelo", [
        ("TempoMinBox", "O <b>andamento</b> mínimo e máximo para a linha selecionada. <i>Qualquer</i> quer dizer sem limite."),
        ("EnergyMinBox", "A <b>energia</b> mínima e máxima."),
        ("ArtistHoursBox", "O mesmo <b>artista</b> não volta antes destas horas. Com 0, vale o descanso definido em Definições da estação → Segurança."),
        ("AlbumHoursBox", "O mesmo <b>álbum</b> não volta antes destas horas. Com 0, não há regra de álbum."),
        ("MaxSlowBox", "No máximo tantas <b>músicas lentas</b> seguidas, ou seja, de andamento 1 ou 2. Com 0, não há limite."),
    ]),
    ("tip", "Se uma regra não deixasse nada para tocar, é posta de lado nessa música: primeiro a do álbum, depois a das lentas, depois a do artista. Assim a hora fica sempre cheia. Se nenhuma música tiver o andamento ou a energia que uma linha pede, a linha toca da categoria toda, e as Playlists diárias avisam."),
    ("h2", "Playlists diárias"),
    ("p", "As playlists diárias são as horas tal como vão tocar, feitas a partir dos modelos: cada música, jingle e anúncio, por ordem, com a sua hora de início."),
    ("fig", "schedule-daily", "Playlists diárias", [
        ("DayBox", "O dia, com ◀ ▶ e <b>Hoje</b>."),
        ("HourList", "As 24 horas do dia, com o modelo e a duração de cada uma. ✎ quer dizer alterada à mão; ↻ quer dizer que o modelo mudou entretanto."),
        ("OnAirText", "O que está no ar agora, e quanto vai adiantado ou atrasado em relação ao plano."),
        ("ItemList", "A hora: músicas, jingles, os anúncios do bloco com o cliente, e os eventos, cada um com a hora de início e de onde veio. ✓ já tocou, ▶ está no ar, OPCIONAL só toca se houver tempo."),
        ("SegueButton", "<b>Transição…</b> mostra como a música selecionada passa para a seguinte, como em Transições; o <b>Editor de mistura…</b>, ao lado, mostra a hora inteira (mais abaixo)."),
        ("StreamButton", "<b>Stream…</b> põe o stream de outra rádio nesta hora, a uma hora certa ou depois do item no ar. Remover tira-o."),
        ("TagsButton", "<b>Editar etiquetas…</b> da música selecionada."),
        ("ReplaceButton", "<b>Outra música</b> troca-a por outra da mesma linha do modelo. ▲ ▼ mudam a ordem e <b>Remover</b> tira."),
        ("NoteButton", "<b>📝 Nota…</b> no item selecionado e <b>📝 Nota da hora…</b> deixam recados para o locutor, mostrados no ar antes e durante o item. Os itens com nota têm 📝 na lista."),
        ("GenerateHourButton", "<b>Refazer esta hora</b> a partir do seu modelo."),
        ("GenerateDayButton", "<b>Refazer o dia</b>. As horas alteradas à mão ficam como estão."),
        ("Tracks", "A biblioteca. Com <b>duplo clique</b> numa música, ela entra a seguir ao item selecionado. O botão direito tem Ouvir."),
        ("ListenButton", "<b>Ouvir</b> o item selecionado, também com duplo clique ou a barra de espaços, sempre na pré-escuta."),
        ("TotalText", "O total da hora: músicas e jingles, blocos e eventos, e o que é opcional."),
    ]),
    ("h3", "O editor de mistura: a hora inteira de uma vista"),
    ("p", "O <b>Editor de mistura…</b> mostra a hora selecionada numa só linha do tempo, cada item onde começa, para ver e ouvir "
          "como a hora corre. As músicas ficam em duas faixas, uma a seguir à outra, para se ver onde cada uma começa por "
          "cima do fim da anterior. A linha vermelha é a hora certa, e a linha por cima diz se a hora acaba mais cedo ou "
          "se passa da hora."),
    ("fig", "schedule-mix", "O editor de mistura", [
        ("Timeline", "A hora. <b>Arraste</b> um item para a esquerda para o começar mais cedo, por cima do fim do anterior; tudo o que vem depois anda com ele. O ✋ marca um item que pôs à mão. A roda desloca e Ctrl + roda aproxima."),
        ("EndsText", "Quando acaba a hora, e a que distância fica da hora certa."),
        ("SelectedText", "O que acontece no início do item selecionado, por palavras simples."),
        ("MovePanel", "Ajuste-o um segundo ou uma décima, ou volte ao crossfade <b>Automático</b>."),
        ("SegueButton", "<b>Transição…</b> abre a passagem no editor de transições. Um duplo clique no item faz o mesmo."),
        ("PlayButton", "Clique em qualquer ponto da linha do tempo para pôr o cursor e depois em <b>Tocar a partir do cursor</b> para ouvir a hora dali em diante, por todas as passagens, tal como vai soar no ar, na pré-escuta (Espaço)."),
        ("ZoomAllButton", "<b>Hora inteira</b> cabe a hora na janela; − e + afastam e aproximam."),
    ]),
    ("p", "Mover um item aqui muda <b>só esta hora</b>. O item anterior toca até ao fim, ao volume normal, como debaixo de um voice "
          "track, o que é mesmo o que se quer para um jingle por cima do final de uma música. Nunca começa mais tarde do que o "
          "crossfade automático, nem mais de 30 segundos por cima do anterior. Os blocos publicitários e os eventos da "
          "programação aparecem, mas não se movem aqui. No ar, a sobreposição só conta por cima do item para que foi definida: "
          "se um bloco publicitário ou um evento ficar pelo meio, o item começa depois dele como de costume, e nunca se toca "
          "por cima de um anúncio."),
    ("tip", "Para mudar como duas músicas passam sempre uma para a outra, onde quer que toquem, use antes <b>Transição…</b>: os "
            "pontos de cue ficam guardados com as músicas. O editor de mistura serve para afinar uma hora em particular."),
    ("list", [
        "<b>Pode construir a hora música a música</b> com Outra música, ▲ ▼, Remover e a biblioteca. Cada alteração fica gravada logo, e a hora fica marcada com ✎. Uma hora assim nunca é refeita sozinha.",
        "<b>Ou pode deixar as regras trabalhar.</b> Hoje e os dias seguintes estão sempre feitos, e uma hora cujo modelo mude é refeita em poucos minutos, a não ser que tenha sido alterada à mão ou esteja prestes a tocar.",
        "As regras de escolha são simples: primeiro as músicas que descansaram mais tempo; uma música não volta antes de N horas e um artista antes de N minutos, como definido em Definições da estação → Segurança; e nunca o mesmo artista duas vezes seguidas se houver outro.",
        "As músicas que o programa de emissão junta para acertar o horário aparecem como <i>juntada na emissão para acertar o horário</i>.",
    ]),
    ("h2", "Voice tracks"),
    ("p", "Com os voice tracks, o locutor grava com antecedência o que diria entre duas músicas, como se estivesse em direto. É "
          "assim que se faz o programa de fim de semana sem estar no estúdio. Selecione uma música numa hora e clique em "
          "<b>Voice track…</b>."),
    ("fig", "voicetrack", "Voice track…", [
        ("InputBox", "A <b>entrada</b> por onde grava: o microfone do estúdio ou outra entrada deste computador."),
        ("PreRollBox", "Quanto do fim da música ouve antes de falar."),
        ("StartButton", "<b>Começar</b> toca o fim da música anterior, na pré-escuta."),
        ("TalkButton", "<b>Falar</b>, ou a barra de espaços, começa a gravar, se quiser por cima do fim da música. Outra vez para parar."),
        ("NextButton", "<b>Música seguinte</b>, ou Enter, faz entrar o item seguinte por baixo da sua voz, enquanto continua a falar."),
        ("OverlapText", "Depois de gravar, ajuste quanto da voz entra por cima do fim da música…"),
        ("NextText", "…e onde entra a música seguinte, em passos de meio segundo."),
        ("PlayBackButton", "<b>Ouvir</b> tal como vai sair no ar, ou <b>Gravar de novo</b>."),
        ("SaveButton", "<b>Guardar</b> deixa um MP3 na pasta de voice tracks da estação e na biblioteca, em <b>Takes</b>."),
    ]),
    ("fig", "schedule-voice", "O voice track na hora", [
        ("ItemList", "O voice track fica logo a seguir à sua música, marcado com 🗣, e começa por cima dos últimos segundos dela."),
        ("VoiceButton", "<b>Voice track…</b> outra vez, na música ou no próprio voice track, para o gravar de novo."),
    ]),
    ("tip", "Um voice track pertence à música que está antes dele. Se tirar ou trocar essa música, o voice track vai com ela; se a mover, ele também se move; e no ar é saltado se a música não puder tocar."),
]),

("Sintoniza Import", "import", [
    ("p", "O Sintoniza Import, que nos ecrãs em português se chama Sintoniza Importação, traz uma pasta de música para a "
          "biblioteca e transforma as pastas que tem lá dentro em categorias. Pede um utilizador com a permissão <b>Biblioteca</b>."),
    ("fig", "import", "Sintoniza Import", [
        ("FolderBox", "A <b>pasta</b> a importar, com <b>Escolher…</b> ao lado."),
        ("NameBox", "A <b>categoria</b> para onde vai. Por omissão, tem o nome da pasta…"),
        ("ParentBox", "…e fica <b>dentro de</b> uma das oito da estação, Music por omissão, ou de uma categoria debaixo delas."),
        ("KindBox", "O <b>tipo</b> das categorias novas, que segue o da categoria onde ficam."),
        ("LevelsBox", "<b>Pastas dentro dela tornam-se subcategorias</b>: nenhuma, ou de 1 a 3 níveis. Por exemplo, a pasta Música/Anos 80 dá Music › Anos 80."),
        ("AlsoInRadio", "O que fazer às músicas que já estão noutra categoria: mantê-las lá e juntar também esta, passá-las para esta, ou deixá-las como estão."),
        ("PreviewButton", "<b>Pré-visualizar</b> mostra o que vai acontecer, sem mudar nada."),
        ("PlanList", "As categorias que vai criar, marcadas NOVA ou JÁ EXISTE, os ficheiros de cada uma, quantos já estão na biblioteca e quantos estão noutra categoria."),
        ("ImportButton", "<b>Importar</b> cria as categorias, lê as músicas e põe cada uma na sua categoria."),
    ]),
    ("p", "Cada pasta fica associada à sua categoria, por isso as músicas que lá forem postas mais tarde também entram nela. "
          "O programa de emissão analisa essas pastas sozinho. <b>Parar</b> interrompe uma importação, e importar de novo termina-a."),
    ("h2", "radio-import, na linha de comandos"),
    ("p", "A mesma análise pode correr sem janela, por exemplo todas as noites pelo cron ou pelo Agendador de Tarefas:"),
    ("pre", "radio-import                          analisa as pastas da biblioteca\n"
            "radio-import /musica /jingles         analisa estas pastas\n"
            "radio-import --db /caminho/library.db um ficheiro de biblioteca SQLite\n"
            "radio-import --mysql utilizador:palavra-passe@servidor[:porta]/basededados\n"
            "radio-import --stats                  o que está na biblioteca"),
    ("p", "Só são lidos os ficheiros novos e os alterados. Os ficheiros apagados, e as músicas de uma pasta apagada, saem da biblioteca. Um disco ou uma partilha que não esteja ligado fica como está."),
]),

("Sintoniza Rec", "rec", [
    ("p", "O Sintoniza Rec é o gravador da estação. Grava tudo o que vai para o ar, todo o dia e todos os dias, e guarda as "
          "gravações. A Lei da Rádio, no artigo 39.º, obriga a gravar as emissões e a guardá-las pelo menos 30 dias."),
    ("p", "Não precisa da biblioteca nem de um utilizador, por isso pode correr no computador do estúdio ou noutro qualquer. "
          "Num computador só para ele, continua a gravar enquanto o do estúdio é reiniciado."),
    ("fig", "rec", "Sintoniza Rec", [
        ("RecText", "<b>● REC</b> enquanto grava, e há quanto tempo. Aparece PARADO a vermelho quando não grava, e SILÊNCIO quando não há som há algum tempo."),
        ("SourceText", "De onde grava, e como."),
        ("MeterL", "O nível do que está a entrar."),
        ("DiskText", "O ficheiro que está a ser escrito, o espaço livre, durante quanto tempo as gravações são guardadas e de quanto em quanto tempo começa um ficheiro novo."),
        ("RecButton", "<b>Gravar</b> e <b>Parar</b>. Fechar a janela para a gravação, por isso o programa pergunta primeiro."),
        ("MonitorButton", "<b>Ouvir em direto</b> o que está a ser gravado, na saída de escuta: auscultadores, nunca uma saída que vá para o ar."),
        ("SettingsButton", "<b>Definições</b>: a origem, os ficheiros, a escuta e os alertas."),
        ("DayBox", "As <b>gravações de um dia</b>. Hoje volta ao dia de hoje…"),
        ("FileList", "…cada ficheiro com o início e o fim, a duração e o tamanho. Faça duplo clique num, ou escolha-o e…"),
        ("PlayButton", "…carregue em <b>▶</b> para o ouvir. A barra por baixo percorre-o e mostra a hora a que cada momento esteve no ar."),
        ("FromBox", "Para <b>exportar um excerto</b>, escolha o dia, a hora de início e de fim e o formato…"),
        ("ExportButton", "…e <b>Exportar…</b> pergunta onde guardar. Sai um só ficheiro, cortado ao segundo, feito de quantas gravações forem precisas. Se o gravador esteve parado nesse período, esse tempo fica de fora, e o programa diz-lho."),
    ]),
    ("h2", "De onde grava"),
    ("fig", "rec-settings", "Definições → Origem", [
        ("StationBox", "O nome da estação, que aparece na janela e nos alertas."),
        ("InputRadio", "Pode gravar uma <b>entrada</b> deste computador…"),
        ("InputBox", "…de preferência a saída de programa da mesa, ligada à placa de som. Assim os microfones também ficam gravados, o que não acontece com o som do próprio programa de emissão."),
        ("StreamRadio", "Ou pode gravar o <b>stream</b> da estação…"),
        ("StreamBox", "…a partir do seu endereço. Se o stream cair, é aberto outra vez sozinho, num ficheiro novo."),
        ("AutostartBox", "<b>Abrir o Sintoniza Rec quando entro neste computador</b>: depois de uma falha de luz ou de um reinício, volta a gravar sozinho."),
    ]),
    ("h2", "Os ficheiros"),
    ("fig", "rec-files", "Definições → Ficheiros", [
        ("FolderBox", "A <b>pasta</b> das gravações. Lá dentro há uma pasta por dia, como 2026-10-21, com os ficheiros, como RE_2026-10-21_10-00-00.mp3. Também pode ser uma pasta partilhada."),
        ("FormatBox", "MP3, ou WAV sem compressão, que ocupa cerca de 10 GB por dia."),
        ("BitrateBox", "A qualidade do MP3. <b>Mono</b> ocupa metade do espaço."),
        ("MinutesBox", "<b>Um ficheiro novo a cada</b> 5 a 60 minutos, a partir de um minuto à escolha. O ficheiro novo começa antes de o anterior acabar, por isso nada se perde entre os dois."),
        ("KeepDaysBox", "<b>Guardadas durante</b>: as gravações mais antigas são apagadas sozinhas. No mínimo 30 dias."),
        ("SpaceText", "Quanto espaço ocupa um dia e o período todo."),
        ("ArchiveBox", "Uma <b>cópia de arquivo</b> opcional, em mono e baixa qualidade, noutra pasta, como um NAS ou outro disco, guardada durante meses ou para sempre."),
    ]),
    ("h2", "Alertas"),
    ("fig", "rec-alerts", "Definições → Alertas", [
        ("SilenceDbBox", "<b>Silêncio</b>: abaixo deste nível durante este tempo, ou um stream que não envia nada. Volta a avisar quando o som regressa."),
        ("AliveBox", "Uma mensagem <b>continuo a gravar</b> uma vez por dia. Se um dia ela não chegar, algo se passa com o computador."),
        ("TelegramTokenBox", "Os alertas seguem por <b>Telegram</b>…"),
        ("SmtpServerBox", "…por <b>e-mail</b>…"),
        ("WebhookBox", "…ou para um <b>endereço web</b>, em JSON. É usado o que estiver preenchido."),
        ("TestAlertButton", "<b>Enviar um teste</b>."),
    ]),
    ("p", "O Rec também avisa quando uma gravação não pode ser escrita, por causa de um disco cheio ou em falta ou de uma partilha que desapareceu, e nesse caso tenta outra vez com um ficheiro novo. Avisa ainda quando restam menos de 2 GB livres."),
]),

("Como faço…", "howto", [
    ("p", "Respostas rápidas às tarefas do dia a dia, passo a passo."),
    ("h3", "…para dar a um locutor o seu próprio acesso?"),
    ("steps", ["Na Pro, abra Estação → <b>Utilizadores</b> e clique em <b>Novo utilizador</b>.", "Escreva o nome e uma palavra-passe e clique em <b>Locutor</b>.",
               "Carregue em <b>Guardar utilizador</b>. A partir daí, essa pessoa pode trabalhar no ar, gravar voice tracks na Pro e usar o controlo remoto web, e mais nada."]),
    ("h3", "…para criar o programa da manhã nos dias úteis?"),
    ("steps", ["Na Pro, abra os <b>Modelos de hora</b>, clique em <b>Novo</b> e dê-lhe um nome.", "Junte as linhas, por exemplo Music › Atuais, Gold, Jingles, Atuais e Anos 80.",
               "Arraste por cima das horas das 7 às 10, de segunda a sexta, e carregue em <b>Guardar</b>.", "Veja o resultado nas <b>Playlists diárias</b>: as horas ficam feitas em poucos minutos."]),
    ("h3", "…para pôr a publicidade no fim da hora?"),
    ("steps", ["Na Pro, abra os <b>Blocos publicitários</b>, clique em <b>Novo</b> e dê ao bloco um alinhamento com uma secção.", "Com <b>Adicionar linha</b>, junte <code>*:00:00</code> e clique nessa hora para a marcar em todos os dias.",
               "Escolha <b>Acaba à sua hora</b>. O bloco acaba exatamente à hora certa e é a última coisa da hora anterior.", "Marque as campanhas em <b>Clientes</b> e carregue em <b>Guardar</b>."]),
    ("h3", "…para pôr o horóscopo de cada manhã?"),
    ("steps", ["Na Pro, abra os <b>Eventos</b>, crie um evento novo e marque-o na semana, por exemplo às 8:15 todos os dias.",
               "Em <b>Uma série: outro ficheiro em certos dias</b>, dê a cada dia da semana a sua gravação, de segunda a domingo.",
               "Para não haver horóscopo no dia de Natal, clique em <b>Juntar uma data</b>, escolha 25 de dezembro e deixe o ficheiro vazio. Carregue em <b>Guardar</b>."]),
    ("h3", "…para mostrar no site a música que está a tocar?"),
    ("steps", ["Na Pro, abra <b>Definições da estação → Site</b> e ligue <b>Enviar o que está a tocar para o site da estação</b>.",
               "Ponha o endereço da página e a chave, ou junte um texto ou a capa em <b>FICHEIROS E FTP</b>.",
               "Em <b>Atrasar cada mudança</b>, ponha o atraso do stream, e carregue em <b>Testar</b> ao lado de cada um para experimentar."]),
    ("h3", "…para trocar uma música às 9:00 de amanhã?"),
    ("steps", ["Na Pro, abra as <b>Playlists diárias</b>, avance com ▶ até amanhã e escolha a hora <b>09:00</b>.", "Selecione a música e carregue em <b>Outra música</b>, ou em <b>Remover</b>, e depois faça duplo clique numa música da biblioteca."]),
    ("h3", "…para pôr uma pasta de músicas no Gold?"),
    ("p", "Abra o Sintoniza Import com essa pasta, escolha a categoria Gold e não crie subcategorias. Em alternativa, no programa de emissão, vá ao separador Pastas, clique na pasta com o botão direito e escolha Categoria e depois Gold."),
    ("h3", "…para enviar à ERC a música do mês passado?"),
    ("steps", ["Na Pro, abra as <b>Quotas ERC</b> e escolha <b>Mês passado</b>.", "Veja as quotas e classifique as músicas que aparecem em <i>Músicas por classificar</i>.",
               "Carregue em <b>Exportar para a ERC (XML)…</b> e envie o ficheiro pelo portal da ERC."]),
    ("h3", "…para tirar o relatório de música para a sociedade de autores?"),
    ("p", "No programa de emissão, abra os <b>Relatórios</b>, escolha <i>Música emitida (direitos de autor)</i> e o mês, e carregue em <b>Guardar como CSV…</b>."),
    ("h3", "…para enviar a um anunciante o comprovativo de emissão?"),
    ("p", "Nos <b>Relatórios</b>, escolha <i>Anúncios (comprovativo de emissão)</i>, o anunciante e as datas, carregue em <b>Guardar como página para imprimir…</b> e imprima para PDF. Na Pro, o <b>Certificado…</b> do cliente dá o mesmo, com o que foi marcado ao lado do que foi para o ar."),
    ("h3", "…para fechar a faturação do mês?"),
    ("p", "Na Pro, em <b>Clientes</b>, carregue em <b>Faturação…</b>. Vem o mês passado por omissão, com os spots de cada cliente campanha a campanha. Guarde em CSV para a contabilidade."),
]),

("Quando algo não está bem", "trouble", [
    ("p", "Os problemas mais comuns e o que fazer em cada caso."),
    ("table", ["O que vê", "O que fazer"], [
        ["A playlist da emissão não corresponde às Playlists diárias", "Carregue em <b>Limpar</b> no programa de emissão. A playlist volta a encher-se a partir do plano, desde o item previsto para esse momento. Se a emissão vai adiantada ou atrasada, a linha vermelha nas Playlists diárias diz quanto, e o programa acerta até à hora seguinte."],
        ["Só tocam jingles e anúncios", "Os ficheiros das músicas mudaram de sítio ou foram apagados. Corrija as pastas das categorias em Pro → Categorias e clique em <b>Analisar</b> no separador Biblioteca."],
        ["Uma música é saltada com “FileOpen”", "O ficheiro não está onde a biblioteca diz. Carregue de novo em <b>Analisar</b>."],
        ["O que oiço na pré-escuta vai para o ar", "Em Definições → Áudio → <b>Pré-escuta</b>, escolha uma saída que não vá para o ar, como os auscultadores ou o PFL."],
        ["Algo correu mal no ar", "Em Definições → Segurança, <b>Abrir a pasta dos registos</b> mostra um ficheiro por dia. Em Definições da estação → Alertas pode pedir que o avisem por Telegram ou e-mail."],
        ["Uma luz de estado está vermelha", "Aponte para ela para ler o que se passa, e clique para abrir as definições correspondentes."],
        ["Um estúdio Windows ou Mac não encontra as músicas", "Em Definições → Biblioteca → <b>Caminhos deste computador</b>, diga onde está a música da biblioteca nesse computador. <b>Testar</b> diz quantas músicas encontra."],
        ["O site não mostra a música que está a tocar", "Veja se a música não está marcada <b>Fora do site</b> e se <b>Só músicas</b> não está a guardar a última música durante os jingles. Lembre-se de que o atraso em <b>Atrasar cada mudança</b> faz o site mudar só uns segundos depois."],
        ["O programa de emissão mostra RESERVA", "Outro computador está no ar, e a barra diz qual. Se deve ser este, carregue em <b>Ir para o ar</b>."],
    ]),
]),
]
