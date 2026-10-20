"""O texto do manual do utilizador, em português (build.py junta tudo). Os blocos são os de text_en.py; os nomes dos
botões e menus são os do programa em português (src/Playout.App/i18n/pt.json)."""

def _date(d):
    months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"]
    return f"{d.day} de {months[d.month - 1]} de {d.year}"

WORDS = {
    "title": "Sintoniza Radio Broadcast system",
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
    ("p", "O Sintoniza é automação de rádio para estações que estão no ar a toda a hora. São três programas que partilham "
          "uma base de dados, a <b>biblioteca</b>: a música e as suas categorias, a programação, as playlists diárias e o "
          "histórico de tudo o que foi para o ar."),
    ("icons", [
        ("radio-playout.png", "Sintoniza Radio Broadcast system", "O programa de emissão, no estúdio: a playlist, o cartwall, o microfone e o stream. É o que o locutor usa."),
        ("radio-playout-schedule.png", "Sintoniza Pro", "O programa de produção da estação: as suas definições, categorias e cartwalls; eventos, blocos publicitários, clientes e campanhas, modelos de hora para os programas e as playlists diárias, música a música."),
        ("radio-playout-import.png", "Sintoniza Importação", "Traz pastas de música para a biblioteca, em categorias."),
    ]),
    ("p", "Cada um tem a sua entrada no menu das aplicações (Linux), no menu Iniciar e no ambiente de trabalho (Windows) ou "
          "em Aplicações (macOS), e só corre uma cópia de cada um por computador. Com uma biblioteca MySQL / MariaDB "
          "partilhada, a Pro e a Importação podem correr noutros computadores (o departamento comercial, a "
          "discoteca); o estúdio vê as alterações em poucos segundos."),
    ("tip", "As <b>Definições</b> do programa de emissão só têm o que é desse computador: placas de som, microfone, a base "
            "de dados, teclas. <b>Tudo o que é da estação</b> — nome e logótipo, crossfades, música de enchimento, sinal horário, "
            "streaming, textos RDS, site, alertas, categorias, cartwalls — <b>define-se uma vez na Sintoniza Pro</b>, para todos os estúdios."),
    ("h2", "Como o dia é planeado e emitido"),
    ("steps", [
        "<b>Importe</b> a música em categorias, debaixo das oito da estação (Music › Atuais, Gold, Anos 80, Jingles, Spots…).",
        "Na <b>Pro</b>, faça <b>modelos de hora</b> para os seus programas (que categorias tocam e por que ordem) e coloque-os na semana; marque os <b>blocos publicitários</b> e os seus <b>clientes</b>.",
        "As <b>playlists diárias</b> são feitas a partir dos modelos para hoje e os próximos dias. Veja-as, troque músicas à mão ou deixe as regras preenchê-las.",
        "O <b>programa de emissão</b> toca as playlists diárias, começa os blocos e os eventos às suas horas e mantém cada hora a horas.",
    ]),
]),

("Primeira configuração", "setup", [
    ("p", "Em <b>cada computador de estúdio</b>, no programa de emissão: clique em <b>Definições</b> (em baixo à esquerda, ou "
          "Ctrl/Cmd + ,). Só lá está o que é desse computador. <b>Guardar</b> aplica as alterações."),
    ("fig", "settings-station", "Definições → Geral", [("LanguageBox", "O <b>idioma</b> dos ecrãs (English, Português, Français); aplica-se da próxima vez que o programa arrancar.")]),
    ("fig", "settings-audio", "Definições → Áudio", [
        ("MusicDeviceBox", "A placa de som e o par de canais da <b>Música</b>; Cartwall e Pré-escuta por baixo. <b>Testar</b> toca um tom curto."),
        ("PreviewDeviceBox", "A <b>Pré-escuta</b> serve para ouvir antes do ar: auscultadores ou o PFL/cue da mesa, nunca uma saída do ar.")]),
    ("fig", "settings-library", "Definições → Biblioteca", [
        ("LibraryTypeBox", "Onde fica a biblioteca: <i>SQLite</i> (um ficheiro neste computador) ou <i>MySQL / MariaDB</i> (um servidor partilhado por vários estúdios)."),
        ("TestLibraryButton", "<b>Testar</b> abre-a (e cria as tabelas da primeira vez).")]),
    ("fig", "settings-safety", "Definições → Segurança", [
        ("ResumeBox", "<b>Continuar a tocar ao arrancar</b>, <b>Voltar a arrancar se falhar</b>, <b>Arrancar quando inicio sessão</b>: para uma estação que nunca sai do ar."),
        ("BackupBox", "<b>Cópias de segurança</b>: todos os dias, da base de dados e dos ficheiros da estação.")]),
    ("p", "Depois, <b>uma vez para toda a estação</b>, na <b>Sintoniza Pro → Definições da estação</b>:"),
    ("fig", "station-station", "Definições da estação → Estação", [("StationNameBox", "<b>Nome da estação</b>, <b>logótipo</b>, género e site: aparecem ao lado do relógio e vão para o stream e o RDS. "
        "<b>Tipo de estação</b>: <i>Rádio FM</i> (microfone pela mesa de mistura) ou <i>Rádio web</i> (pelo computador).")]),
    ("fig", "station-audio", "Definições da estação → Áudio", [("CrossfadeBox", "Crossfade de música para música, em segundos."),
        ("OtherCrossfadeBox", "Crossfade para dentro ou para fora de jingles, anúncios e programas (0 = corte limpo)."),
        ("LoudnessBox", "Tudo ao mesmo volume percebido (EBU R128).")]),
    ("fig", "station-safety", "Definições da estação → Segurança", [
        ("SilenceBox", "<b>Detetar silêncio</b>: um alarme (e, em AUTO, um salto) quando não se ouve nada."),
        ("FillBox", "<b>Música de enchimento</b>: o que toca se a playlist alguma vez acabar, e os tempos de descanso (uma música não volta antes de N horas, um artista antes de N minutos). As playlists diárias usam os mesmos tempos.")]),
    ("p", "Os outros separadores: <b>Sinal horário</b>, <b>Biblioteca</b> (outras pastas de música), <b>Streaming</b>, <b>RDS</b> (os textos), "
          "<b>Site</b>, <b>Alertas</b> e <b>Playlist</b> (dias feitos com antecedência, cores das linhas). Todos os estúdios recebem as "
          "definições da estação até um minuto depois de guardar. O primeiro estúdio a arrancar depois de atualizar para esta versão publica as suas."),
    ("tip", "Depois importe a música (capítulo 5), faça as cartwalls, os modelos e os blocos (capítulo 4), ponha o programa de emissão em <b>AUTO</b> e carregue em <b>Play</b>."),
]),

("O programa de emissão", "onair", [
    ("p", "O <b>Sintoniza Radio Broadcast system</b> é o programa com que o locutor trabalha. A metade de cima é o que está "
          "no ar agora; a de baixo é o que vem a seguir (a playlist) e o que está à mão (cartwall, biblioteca, pastas)."),
    ("fig", "main", "O ecrã de emissão", [
        ("OnAirBadge", "Estado: <b>NO AR</b>, <b>PARADO</b>, <b>SILÊNCIO</b> (não se ouve nada) ou a contagem para o próximo bloco."),
        ("NowTitle", "O que está no ar, com o artista."),
        ("WaveMain", "A forma de onda: a parte vermelha já tocou. A barra fina por baixo é a música inteira."),
        ("Elapsed", "Tempo decorrido, e o <b>SEGUINTE</b> por baixo."),
        ("PlayButton", "<b>Play</b> (F5)."),
        ("PauseButton", "<b>Pausa</b> / continuar (F6)."),
        ("NextButton", "<b>Seguinte</b> (F7): crossfade para o próximo item já."),
        ("StopButton", "<b>Parar</b> (F8)."),
        ("Remaining", "Tempo que falta ao que está no ar."),
        ("AutoSwitch", "<b>AUTO</b> segue a playlist; <b>ASSISTIDO</b> para depois de cada item até começar o seguinte."),
        ("Clock", "O relógio, o nome e o logótipo da estação."),
        ("TopOfHourText", "Contagem para a hora certa."),
        ("PlaylistEndsText", "Quando acaba a playlist."),
        ("NextEventPanel", "O próximo evento ou bloco programado."),
        ("MeterL", "Níveis de saída, esquerdo e direito."),
        ("Playlist", "A playlist (páginas seguintes)."),
        ("CartwallTab", "Separadores <b>CARTWALL</b>, <b>BIBLIOTECA</b> e <b>PASTAS</b>."),
        ("Cartwall", "O cartwall: jingles e sons à mão."),
        ("SettingsButton", "<b>Definições</b> (Ctrl/Cmd + ,)."),
        ("ReportsButton", "<b>Relatórios</b>: música emitida e comprovativo de emissão."),
        ("LastLog", "A última coisa que aconteceu (tudo fica no registo)."),
    ]),
    ("h2", "A playlist"),
    ("p", "Cada linha mostra um ícone do tipo (♫ música, ✦ jingle, € anúncio, 🎙 programa, stream), o título e o artista, a "
          "hora prevista de <b>início</b> e a <b>duração</b>. Tudo o que não é música tem a sua própria cor de fundo "
          "(Definições → Playlist). O que está no ar enche-se de vermelho à medida que toca, e uma linha marca onde começa cada hora."),
    ("fig", "main", "Botões da playlist", [
        ("OnAirButton", "<b>No ar</b>: volta ao que está no ar (acende-se quando se sai dele)."),
        ("HourViewButton", "<b>Esta hora</b>: a hora anterior, esta hora e os primeiros 30 minutos da seguinte. Desligado: tudo o que falta tocar."),
        ("AddStreamButton", "<b>Stream…</b> de outra rádio; ao lado <b>Abrir…</b> e <b>Guardar…</b> (playlists M3U) e ▲ ▼ para mover. As músicas vêm da Biblioteca à direita."),
        ("RemoveButton", "<b>Remover</b> os itens selecionados."),
        ("ClearButton", "<b>Limpar</b>: esvazia a playlist exceto o que está no ar e o que a programação bloqueou; o plano volta a enchê-la."),
    ], True),
    ("list", [
        "<b>Duplo clique</b> num item (ou Enter) para o pôr no ar já.",
        "<b>Arraste</b> linhas para as mudar de ordem, ou largue músicas da biblioteca, do separador Pastas ou do gestor de ficheiros.",
        "<b>Botão direito</b>: <i>Editar pontos de cue…</i> (Ctrl/Cmd+E), <i>Editar etiquetas…</i> (Ctrl/Cmd+T), <i>Remover</i>.",
        "As linhas marcadas <b>PROGRAMADO</b> são blocos e eventos que vêm aí, mostrados onde vão tocar.",
    ]),
    ("warn", "<b>Os anúncios estão protegidos.</b> Enquanto um anúncio está no ar, Seguinte, Pausa, Parar e pôr outro item no ar "
             "perguntam primeiro. Só um clique no botão da pergunta o faz, para que um spot pago não seja cortado por engano."),
    ("p", "<b>De onde vêm as músicas:</b> das playlists diárias (capítulo 4). Cerca de meia hora antes de cada hora, as suas "
          "músicas entram na playlist. Perto do fim da hora o programa acerta o horário: se a hora seguinte fosse começar "
          "tarde, um item <i>opcional</i> fica de fora; se fosse começar cedo, juntam-se músicas. Pode sempre alterar a playlist no ar."),
    ("h2", "Cartwall"),
    ("fig", "main", "O cartwall", [
        ("Cartwall", "Os carts: nome, ícone e cor; o número é a tecla do cart (Definições → Atalhos)."),
        ("CartwallPicker", "Que cartwall é mostrado."),
    ], True),
    ("list", [
        "<b>Clique</b> num cart para o tocar, outra vez para o parar. Os carts tocam por cima da música, na saída Cartwall. Cada um mostra quanto dura; a tocar, o tempo que falta.",
        "As cartwalls são da estação: fazem-se na Sintoniza Pro → <b>Cartwalls</b>, e todos os estúdios as recebem até um minuto depois.",
        "Teclas para cada cart: Definições → Atalhos.",
    ]),
    ("h2", "Biblioteca"),
    ("fig", "main-library", "O separador Biblioteca (Ctrl/Cmd + F)", [
        ("SearchBox", "Pesquise em todas as músicas enquanto escreve: artista, título, álbum ou nome do ficheiro (maiúsculas e acentos não contam)."),
        ("CategoryBox", "Só uma categoria (uma categoria inclui as que estão dentro dela)."),
        ("EditCategoriesButton", "Editar as categorias."),
        ("TrackList", "As músicas. <b>Duplo clique</b> ou Enter: para o fim da playlist. <b>Botão direito</b>: Tocar a seguir, Categoria, Também em, Andamento, Voz, Idioma, Editar etiquetas…, Editar pontos de cue…"),
        ("ScanButton", "<b>Analisar</b>: pôr a biblioteca em dia com as pastas de música já."),
    ]),
    ("fig", "main-folders", "O separador Pastas", [("Library", "As pastas de música no disco: arraste ficheiros ou pastas para a playlist. Botão direito numa pasta → <i>Categoria</i> põe todas as suas músicas (e as que lá forem postas depois) numa categoria.")]),
    ("h2", "Pontos de cue"),
    ("fig", "cue-editor", "Editar pontos de cue…", []),
    ("p", "Marque o <b>Cue In</b> (onde começa), a <b>Intro</b> (fim da introdução, para falar por cima), o <b>Outro</b>, o "
          "<b>Mix</b> (onde começa o item seguinte) e o <b>Cue Out</b>. <i>Detetar</i> encontra o primeiro e o último som e "
          "um bom ponto de Mix. Espaço toca e pausa na saída de pré-escuta; ← → avançam um segundo (Shift: 0,1 s); a roda do rato faz zoom."),
    ("h2", "Transições"),
    ("p", "Como uma música passa para a seguinte: botão direito num item da playlist → <i>Transição para o seguinte…</i> (Ctrl/Cmd+G), ou <b>Transição…</b> nas Playlists diárias."),
    ("fig", "segue", "Transição para o seguinte…", [
        ("Join", "O fim da primeira música (em cima) e o início da seguinte (em baixo), unidas no 0. <b>Arraste</b> qualquer uma para mover a transição; a linha amarela é o nível da primeira (o seu fade)."),
        ("MixText", "<b>Mix</b>: onde começa a música seguinte. Movê-lo cria um marcador Mix: a primeira música continua então no nível total até ao seu Cue Out. <i>Automático</i> volta ao crossfade habitual."),
        ("OutText", "<b>Cue Out</b>: onde a primeira música para."),
        ("InText", "<b>Cue In</b>: onde a música seguinte começa no seu ficheiro (não depois da Intro)."),
        ("PlayButton", "<b>Ouvir a transição</b> desde uns segundos antes, tal como no ar, na saída de pré-escuta (Espaço). Mude algo enquanto toca e ouve logo de novo."),
        ("SummaryText", "O que vai acontecer, por palavras."),
    ]),
    ("p", "Guardado como pontos de cue das duas músicas: valem onde quer que toquem, em todos os estúdios que partilham a biblioteca."),
    ("h2", "Etiquetas"),
    ("fig", "tag-editor", "Editar etiquetas…", []),
    ("p", "Título, artista, álbum, género, ano, número da faixa, comentário e capa, gravados no ficheiro. Um ficheiro que está no ar só pode ser alterado depois de acabar."),
    ("h2", "Relatórios"),
    ("fig", "reports-adverts", "Relatórios", [
        ("KindBox", "<b>Música emitida</b> (direitos de autor), <b>Totais de música por canção</b>, <b>Anúncios</b> (comprovativo de emissão) ou <b>Tudo</b> (registo de emissão)."),
        ("AdvertiserBox", "Para o comprovativo de emissão: um anunciante ou todos."),
        ("HtmlButton", "<b>Guardar como página para imprimir…</b> (ou em PDF); ao lado, <b>Guardar como CSV…</b> para uma folha de cálculo."),
    ]),
    ("h2", "Controlo remoto web"),
    ("p", "Controle o playout a partir de um telemóvel ou de outro computador da rede da estação: uma página web, sem nada para instalar."),
    ("fig", "settings-remote", "Definições → Remoto", [
        ("RemoteBox", "Ligue-o (está desligado até o fazer)."),
        ("RemotePasswordBox", "A <b>palavra-passe</b> que a página pede (e a porta)."),
        ("RemoteAddressText", "O endereço a abrir no telemóvel, na mesma rede."),
    ]),
    ("fig", "remote-phone", "O controlo remoto num telemóvel", []),
    ("list", [
        "O que está no ar, o tempo que falta e o próximo bloco; luzes do stream e do silêncio.",
        "<b>▶ ❚❚ ⏭ ■</b> e <b>AUTO</b>, como no ecrã. Um anúncio no ar só é cortado depois de uma pergunta.",
        "<b>A seguir</b>: <i>Já</i> põe um item no ar, <b>✕</b> tira-o. Os carts da cartwall disparam com um toque.",
        "<b>Juntar uma música</b>: pesquise na biblioteca e junte-a a seguir ao item no ar.",
    ]),
    ("warn", "É HTTP simples, para a rede da própria estação. De fora, use-o só pela VPN da estação — nunca abra a porta no router. Cinco palavras-passe erradas bloqueiam a página um minuto."),
    ("h2", "O microfone (rádio web)"),
    ("p", "Com <i>Rádio web</i> escolhido (Pro → Definições da estação), o botão <b>MIC</b> (F9) abre o microfone e a música e os carts "
          "baixam enquanto está aberto: manter premido para falar, ou premir para ligar e premir para desligar (Definições → "
          "Microfone). Com <i>Rádio FM</i> o microfone passa pela mesa."),
    ("h2", "Todas as definições"),
    ("p", "As deste computador, nas <b>Definições</b> do programa de emissão:"),
    ("table", ["Separador", "Para que serve"], [
        ["Geral", "O idioma dos ecrãs"],
        ["Áudio", "Saídas da Música / Cartwall / Pré-escuta"],
        ["Microfone", "(rádio web) entrada, níveis, quanto baixa a música"],
        ["Segurança", "Arranque e falhas, o registo, cópias de segurança"],
        ["Biblioteca", "A base de dados, análises automáticas"],
        ["Atalhos", "Teclas para Play, Pausa, Seguinte, Parar, MIC, o stream e cada cart"],
        ["RDS", "Se este computador envia para o codificador RDS, e como chega a ele"],
        ["Playlist", "Quanto da hora seguinte a playlist mostra"],
        ["Remoto", "O controlo remoto web: ligar/desligar, porta, palavra-passe, o endereço"],
        ["Acerca de", "Versão, atualizações e este manual"],
    ]),
    ("p", "As da estação, na <b>Sintoniza Pro → Definições da estação</b>:"),
    ("table", ["Separador", "Para que serve"], [
        ["Estação", "Nome, logótipo, género, site, rádio FM ou web, símbolo da moeda"],
        ["Áudio", "Crossfades, nivelamento de volume"],
        ["Sinal horário", "Bips ou um ficheiro à hora certa; anúncios de hora (HRS05.mp3 às 05:00…)"],
        ["Segurança", "Deteção de silêncio, música de enchimento"],
        ["Biblioteca", "Outras pastas de música (músicas sem categoria)"],
        ["Streaming", "Servidor Icecast / Shoutcast, formato e bitrate"],
        ["RDS", "PS, radiotexto, RT+"],
        ["Site", "O que está no ar enviado para o site da estação"],
        ["Alertas", "Alertas por Telegram, e-mail ou web: silêncio, stream em baixo, falhas, disco cheio, relógio, cópias"],
        ["Playlist", "Quantos dias de playlists diárias são feitos com antecedência, cores das linhas"],
    ]),
]),

("Sintoniza Pro", "schedule", [
    ("p", "O programa de produção da estação: as suas definições, biblioteca, categorias, cartwalls, programação e publicidade. Abre no <b>Início</b>, com a estação num relance."),
    ("fig", "production", "Sintoniza Pro: Início", [
        ("MainMenu", "O <b>menu</b>: Ficheiro, Biblioteca, Programação, Publicidade, Estação, Ajuda."),
        ("SaveButton", "A <b>barra de ferramentas</b>: Guardar (Ctrl+S) e as ferramentas mais usadas."),
        ("NavPanel", "As <b>ferramentas</b>: Início; Biblioteca (músicas, categorias, importar); Programação (modelos de hora, playlists diárias, eventos); Publicidade (blocos, clientes e campanhas); Estação (definições, cartwalls)."),
        ("HomeClock", "O relógio e a data."),
        ("HomeToday", "<b>Hoje</b>: as horas planeadas, blocos e anúncios marcados, eventos, voice tracks."),
        ("HomeWarnings", "<b>A verificar</b>: ficheiros em falta, músicas sem categoria, horas sem modelo, campanhas a acabar esta semana, alterações por guardar."),
        ("HomeComputer", "<b>Este computador</b>: sistema, processador, memória, disco livre, há quanto tempo está ligado. Ao lado, a biblioteca e a base de dados."),
        ("NowTitleText", "<b>A tocar</b> no ar (do histórico de todos os estúdios), e o modelo desta hora."),
        ("BoxStationText", "A estação, a base de dados e quem está a trabalhar aqui."),
        ("StatusVersion", "A <b>barra de estado</b>: versão, estação, base de dados, utilizador, alterações por guardar…"),
        ("StatusClock", "…e a data e a hora."),
    ]),
    ("h2", "Categorias"),
    ("p", "Todas as estações têm as mesmas oito categorias no topo, que não podem mudar de nome, de sítio nem ser apagadas: "
          "<b>Music</b>, <b>Jingles</b>, <b>Promos</b>, <b>ProgAutor</b> (programas gravados fora), <b>Fillers</b>, <b>Redação</b> "
          "(noticiário), <b>Spots</b> (anúncios) e <b>Takes</b> (inserções de voz: as horas, a temperatura… como se houvesse um "
          "locutor). As suas ficam debaixo delas."),
    ("fig", "categories-fixed", "Categorias: uma das oito da estação", [
        ("CategoryList", "As categorias, em árvore; 🔒 = uma das oito da estação."),
        ("FixedPanel", "Para que serve; só mudam a cor e as pastas."),
        ("AddChildButton", "<b>Nova dentro desta</b>: uma categoria sua debaixo da selecionada (Music › Anos 80, Spots › Clientes…)."),
    ]),
    ("fig", "categories", "Categorias: uma categoria sua", [
        ("ParentBox", "<b>Dentro de</b>: mudá-la para dentro de outra categoria."),
        ("Swatches", "A sua cor nas listas."),
        ("FolderList", "As suas <b>pastas</b>: as músicas delas estão nesta categoria, e as que lá forem postas depois também."),
        ("ApplyButton", "<b>Guardar alterações</b>."),
    ]),
    ("h2", "Cartwalls"),
    ("fig", "cartwalls", "Cartwalls", [
        ("PageList", "As cartwalls: <b>Nova</b>, <b>Copiar</b>, <b>Mudar o nome…</b>, <b>Apagar</b>."),
        ("CartGrid", "Os seus 12 carts, como o estúdio os vê: clique num para o alterar, ou largue um ficheiro nele."),
        ("ChooseFileButton", "O som do cart: <b>Escolher ficheiro…</b>, <b>Ouvir</b> (saída de pré-escuta), <b>Limpar</b>."),
        ("ColorPanel", "A sua cor…"),
        ("IconPanel", "…e o ícone."),
        ("SaveButton", "<b>Guardar</b>: todos os estúdios recebem-nas até um minuto depois."),
    ]),
    ("tip", "Use ficheiros a que todos os estúdios cheguem (uma pasta partilhada), como para a música."),
    ("h2", "A programação"),
    ("p", "Tudo o que se planeia com antecedência: Eventos, Blocos publicitários, Clientes e campanhas, Modelos de hora e Playlists diárias, escolhidos à esquerda. <b>Guardar</b> (na barra de ferramentas, acesa quando algo mudou, ou Ctrl+S) grava na base de "
          "dados e o programa de emissão recebe-o em poucos segundos. Se outra pessoa gravou entretanto, é-lhe perguntado antes de substituir o que ela fez."),
    ("tip", "<b>Os calendários da semana</b> (Eventos, Blocos publicitários, Playlists): <b>clique numa célula</b> para pôr o "
            "item selecionado nesse dia e hora (outra vez para o tirar), <b>arraste</b> para várias células, <b>clique numa hora</b> "
            "para todos os dias. <b>Adicionar linha</b> junta uma hora exata, p. ex. <code>12:59:55</code>; <code>*:59:55</code> é esse minuto em todas as horas."),
    ("h2", "Eventos"),
    ("fig", "schedule-events", "Eventos: o que toca a horas certas", [
        ("EventList", "Os eventos. <b>Novo</b> / <b>Apagar</b> por baixo."),
        ("EventGrid", "A semana: onde o evento selecionado toca."),
        ("EventAddTimeBox", "Uma hora exata para juntar como linha."),
        ("SaveButton", "<b>Guardar</b> toda a programação."),
    ]),
    ("p", "Um evento toca um <b>ficheiro</b>, o <b>ficheiro mais recente de uma pasta</b> (o noticiário: saltado se tiver mais de "
          "N minutos) ou um <b>stream</b> durante N minutos. Como começa: <b>Flexível</b> (depois de acabar o que está no ar, no "
          "máximo N minutos atrasado), <b>Rígido</b> (ao segundo; o que está no ar desvanece) ou <b>Acaba à sua hora</b> (as "
          "músicas antes dele são ajustadas). E o que o locutor pode fazer com ele na playlist: remover e mover, só mover, ou nada."),
    ("h2", "Blocos publicitários"),
    ("fig", "schedule-breaks", "Blocos publicitários", [
        ("BreakList", "Os blocos."),
        ("BreakGrid", "Onde estão na semana (aqui a todas as horas aos :50)."),
        ("PartList", "A <b>ordem de emissão</b>: jingles e <b>secções</b>; os anúncios marcados numa secção tocam aí."),
    ]),
    ("p", "Um jingle pode ser marcado <i>tocar só quando a secção a seguir tiver spots</i> (abertura de secção). Um bloco sem nada marcado é saltado. Os blocos ficam bloqueados na playlist por omissão."),
    ("h2", "Clientes e campanhas"),
    ("fig", "schedule-clients", "Clientes", [
        ("ClientList", "Os <b>clientes</b> (o nome no comprovativo de emissão)."),
        ("CampaignList", "As suas <b>campanhas</b>: os spots (vários vão alternando), secção, posição, primeiro e último dia."),
        ("SpotGrid", "Os blocos da semana: quantos anúncios e quanto dura cada um. <b>Clique</b> num bloco para marcar a campanha lá, todas as semanas ou só nessa data."),
    ]),
    ("p", "<b>Em todos os blocos</b> marca uma campanha em todos os blocos que têm a sua secção. <b>Posição</b>: automática, 1–5 desde o início da secção, ou 6–10 no fim."),
    ("h2", "Playlists: modelos de hora para os programas"),
    ("fig", "schedule-templates", "Modelos de hora", [
        ("TemplateList", "Os modelos: um por programa ou tipo de hora."),
        ("TemplateGrid", "As horas em que cada um toca: aqui as Manhãs nos dias úteis das 07 às 10."),
        ("LineList", "As suas <b>linhas</b>, tocadas por ordem e depois de novo do início até a hora ficar cheia."),
        ("LineCategoryBox", "Uma linha é uma música de uma <b>categoria da biblioteca</b> (Music › Atuais, Gold, Jingles…)…"),
        ("TempoMinBox", "As músicas da linha selecionada: um intervalo de <b>tempo</b> e <b>energia</b> (aqui a primeira música da manhã é animada)."),
        ("AddFolderLineButton", "…ou um ficheiro de uma <b>pasta</b>."),
    ]),
    ("h2", "Regras musicais"),
    ("p", "Dê às músicas um <b>tempo</b> (1 lento … 5 rápido) e uma <b>energia</b> (1 calma … 5 animada) na biblioteca: selecione-as, botão direito → <i>Tempo</i> ou <i>Energia</i>. Depois as linhas de um modelo podem pedi-los, e o modelo afasta artistas e álbuns."),
    ("fig", "schedule-rules", "As regras musicais de um modelo", [
        ("TempoMinBox", "<b>Tempo</b> de … a … para a linha selecionada (<i>qualquer</i> = sem limite)."),
        ("EnergyMinBox", "<b>Energia</b> de … a …."),
        ("ArtistHoursBox", "O mesmo <b>artista</b> não volta antes destas horas (0 = o descanso em Definições → Segurança)."),
        ("AlbumHoursBox", "O mesmo <b>álbum</b> não volta antes destas horas (0 = sem regra de álbum)."),
        ("MaxSlowBox", "No máximo estas <b>músicas lentas</b> (tempo 1–2) seguidas (0 = sem limite)."),
    ]),
    ("tip", "Uma regra que não deixaria nada para tocar é largada nessa música (primeiro o álbum, depois as lentas, depois o artista), por isso a hora fica sempre cheia. Uma linha cujo tempo ou energia nenhuma música tem toca de toda a categoria, e as Playlists diárias avisam."),
    ("h2", "Playlists diárias"),
    ("p", "As horas tal como vão tocar, feitas a partir dos modelos: cada música, jingle e anúncio, por ordem, com a sua hora de início."),
    ("fig", "schedule-daily", "Playlists diárias", [
        ("DayBox", "O dia: ◀ ▶ e <b>Hoje</b>."),
        ("HourList", "As suas 24 horas, com o modelo e a duração de cada uma. ✎ = alterada à mão; ↻ = o seu modelo mudou entretanto."),
        ("OnAirText", "O que está no ar agora, e quanto vai adiantado ou atrasado em relação ao plano."),
        ("ItemList", "A hora: músicas, jingles, os anúncios do bloco (com o cliente) e eventos, cada um com a hora de início e de onde veio. ✓ = já tocou; ▶ = no ar; OPCIONAL = só se houver tempo."),
        ("SegueButton", "<b>Transição…</b>: como a música selecionada passa para a seguinte (ver Transições)."),
        ("StreamButton", "<b>Stream…</b>: o stream de outra rádio a uma hora desta hora, à hora certa ou depois do item no ar. Remover tira-o."),
        ("TagsButton", "<b>Editar etiquetas…</b> da música selecionada."),
        ("ReplaceButton", "<b>Outra música</b> da mesma linha do modelo; ▲ ▼ movem, <b>Remover</b> tira."),
        ("GenerateHourButton", "<b>Refazer esta hora</b> a partir do seu modelo."),
        ("GenerateDayButton", "<b>Refazer o dia</b> (as horas alteradas à mão ficam)."),
        ("Tracks", "A biblioteca: <b>duplo clique</b> numa música para a pôr a seguir ao item selecionado; botão direito → Ouvir."),
        ("ListenButton", "<b>Ouvir</b> o item selecionado (ou duplo clique nele, ou Espaço), só na saída de pré-escuta."),
        ("TotalText", "O total da hora: músicas e jingles, blocos e eventos, e o que é opcional."),
    ]),
    ("list", [
        "<b>Construa-a música a música</b> com Outra música, ▲ ▼, Remover e a biblioteca. Cada alteração fica gravada logo e a hora fica marcada ✎; nunca é refeita sozinha.",
        "<b>Ou deixe as regras fazê-lo</b>: hoje e os próximos dias estão sempre feitos, e uma hora cujo modelo muda é refeita em poucos minutos (exceto se foi alterada à mão ou está prestes a tocar).",
        "As regras: primeiro as músicas que descansaram mais tempo; uma música não volta antes de N horas, um artista antes de N minutos (Definições da estação → Segurança); nunca o mesmo artista duas vezes seguidas se houver outro.",
        "As músicas que o programa de emissão junta para acertar o horário aparecem como <i>juntada na emissão para acertar o horário</i>.",
    ]),
    ("h2", "Voice tracks"),
    ("p", "Grave as suas locuções entre duas músicas com antecedência, como se estivesse em direto: selecione uma música numa hora e clique em <b>Voice track…</b>."),
    ("fig", "voicetrack", "Voice track…", [
        ("InputBox", "A <b>entrada</b> de onde gravar (o microfone do estúdio, ou qualquer entrada deste computador)."),
        ("PreRollBox", "Quanto do fim da música ouve primeiro."),
        ("StartButton", "<b>Começar</b> toca o fim da música anterior, na saída de pré-escuta."),
        ("TalkButton", "<b>Falar</b> (Espaço) começa a gravar — por cima do fim da música, se quiser; outra vez para parar."),
        ("NextButton", "<b>Música seguinte</b> (Enter) faz entrar o item seguinte enquanto fala, por baixo da sua voz."),
        ("OverlapText", "Depois de gravar: quanto começa por cima do fim da música…"),
        ("NextText", "…e onde entra a música seguinte. ±0,5 s para os acertar."),
        ("PlayBackButton", "<b>Ouvir</b> como no ar; <b>Gravar de novo</b> se quiser."),
        ("SaveButton", "<b>Guardar</b>: fica um MP3 na pasta de voice tracks da estação e na biblioteca em <b>Takes</b>."),
    ]),
    ("fig", "schedule-voice", "O voice track na hora", [
        ("ItemList", "Fica logo a seguir à sua música (🗣), a começar por cima dos últimos segundos dela."),
        ("VoiceButton", "<b>Voice track…</b> outra vez na música, ou no voice track, para o gravar de novo."),
    ]),
    ("tip", "Um voice track pertence à música antes dele: tirar ou trocar essa música tira o voice track também, mover a música move-o, e no ar é saltado se a música não puder tocar."),
]),

("Sintoniza Importação", "import", [
    ("p", "Traz uma pasta de música para a biblioteca, com as pastas lá dentro como categorias."),
    ("fig", "import", "Sintoniza Importação", [
        ("FolderBox", "A <b>pasta</b> a importar (<b>Escolher…</b> ao lado)."),
        ("NameBox", "A <b>categoria</b> para onde vai (por omissão o nome da pasta)…"),
        ("ParentBox", "…<b>dentro de</b> uma das oito da estação (Music por omissão) ou de uma categoria debaixo delas."),
        ("KindBox", "O <b>tipo</b> das categorias novas: segue o daquela onde ficam."),
        ("LevelsBox", "<b>Pastas dentro dela tornam-se subcategorias</b>: nenhuma, ou 1 a 3 níveis (Música/Anos 80 → Music › Anos 80)."),
        ("AlsoInRadio", "Músicas que já estão noutra categoria: manter e juntar também esta, passá-las para esta, ou deixá-las como estão."),
        ("PreviewButton", "<b>Pré-visualizar</b>: o que vai acontecer. Ainda nada é alterado."),
        ("PlanList", "As categorias que vai criar (NOVA ou JÁ EXISTE), os ficheiros de cada uma, quantos já estão na biblioteca e quantos estão noutra categoria."),
        ("ImportButton", "<b>Importar</b>: cria as categorias, lê as músicas e põe cada uma na sua categoria."),
    ]),
    ("p", "Cada pasta fica associada à sua categoria: as músicas que lá forem postas mais tarde também a recebem (o programa de "
          "emissão analisa essas pastas sozinho). <b>Parar</b> interrompe uma importação; importar de novo termina-a."),
    ("h2", "radio-import (linha de comandos)"),
    ("p", "A mesma análise sem janela, p. ex. todas as noites pelo cron ou pelo Agendador de Tarefas:"),
    ("pre", "radio-import                          analisa as pastas da biblioteca\n"
            "radio-import /musica /jingles         analisa estas pastas\n"
            "radio-import --db /caminho/library.db um ficheiro de biblioteca SQLite\n"
            "radio-import --mysql utilizador:palavra-passe@servidor[:porta]/basededados\n"
            "radio-import --stats                  o que está na biblioteca"),
    ("p", "Só são lidos os ficheiros novos e alterados. Os ficheiros apagados, e as músicas de uma pasta apagada, saem da biblioteca; um disco ou partilha que não está ligado fica como está."),
]),

("Como faço…", "howto", [
    ("h3", "…para criar um programa da manhã nos dias úteis?"),
    ("steps", ["Pro → <b>Playlists</b> → <b>Novo</b>; dê-lhe um nome.", "Junte as linhas: p. ex. Music › Atuais, Gold, Jingles, Atuais, Anos 80.",
               "Arraste sobre as horas das 07 às 10, de segunda a sexta. <b>Guardar</b>.", "Veja-o nas <b>Playlists diárias</b>: as horas ficam feitas em poucos minutos."]),
    ("h3", "…para pôr a publicidade no fim da hora?"),
    ("steps", ["Pro → <b>Blocos publicitários</b> → <b>Novo</b>; dê-lhe uma ordem de emissão com uma secção.", "<b>Adicionar linha</b> <code>*:59:00</code> e clique nessa hora para todos os dias.",
               "Escolha <b>Acaba à sua hora</b>, para acabar à hora certa.", "Marque as campanhas no separador <b>Clientes</b>. <b>Guardar</b>."]),
    ("h3", "…para trocar uma música nas 09:00 de amanhã?"),
    ("steps", ["Pro → <b>Playlists diárias</b> → ▶ até amanhã → <b>09:00</b>.", "Selecione a música → <b>Outra música</b>, ou <b>Remover</b> e duplo clique numa da biblioteca."]),
    ("h3", "…para pôr uma pasta de músicas no Gold?"),
    ("p", "Sintoniza Importação com essa pasta, <i>na categoria</i> Gold, sem subcategorias. Ou no programa de emissão: separador Pastas → botão direito na pasta → Categoria → Gold."),
    ("h3", "…para tirar o relatório de música para a sociedade de autores?"),
    ("p", "Programa de emissão → <b>Relatórios</b> → <i>Música emitida (direitos de autor)</i> → o mês → <b>Guardar como CSV…</b>."),
    ("h3", "…para enviar a um anunciante o comprovativo de emissão?"),
    ("p", "<b>Relatórios</b> → <i>Anúncios (comprovativo de emissão)</i> → o anunciante e as datas → <b>Guardar como página para imprimir…</b> → imprimir para PDF."),
]),

("Quando algo não está bem", "trouble", [
    ("table", ["O que vê", "O que fazer"], [
        ["A playlist da emissão não corresponde às Playlists diárias", "Carregue em <b>Limpar</b> no programa de emissão: volta a encher-se a partir do plano, desde o item planeado para esse momento. Se vai adiantada ou atrasada, a linha vermelha nas Playlists diárias diz quanto; acerta até à hora seguinte."],
        ["Só tocam jingles e anúncios", "Os ficheiros das músicas mudaram de sítio ou foram apagados: corrija as pastas das categorias (Pro → Categorias) e clique em <b>Analisar</b> no separador Biblioteca."],
        ["Uma música é saltada com “FileOpen”", "O ficheiro não está onde a biblioteca diz: <b>Analisar</b> de novo."],
        ["O que oiço na pré-escuta vai para o ar", "Definições → Áudio → <b>Pré-escuta</b>: escolha uma saída que não seja do ar (auscultadores, PFL)."],
        ["Algo correu mal no ar", "Definições → Segurança → <b>Abrir a pasta dos registos</b>: um ficheiro por dia. Definições → Alertas pode avisá-lo por Telegram ou e-mail."],
    ]),
]),
]
