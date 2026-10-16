# BIM Copiloto · Revit 2027 · v2.1

Assistente local com conversa, imagens e modelagem assistida. Interface em português.
Código e executáveis para a instalação de Revit 2027/.NET 10 desta máquina; outras versões não foram validadas.

## O que está implementado

- Conversa contínua com respostas aparecendo progressivamente, interrupção e histórico local por projeto.
- Seletor de provedor: OpenAI Responses API ou Google Gemini 3.8 Flash.
- Até quatro imagens por mensagem: escolher arquivo, arrastar, colar ou capturar a vista ativa.
- Contexto do modelo ativo: seleção, parâmetros, ambientes, níveis, tipos, materiais, avisos e outras informações disponíveis no resumo.
- Aba **Planta por imagem**: reconhecimento de paredes, portas e janelas, traçado sobre a imagem, correção manual, escala por dois pontos e conversão para ações da Revit API.
- Propostas de edição estruturadas: paredes, pisos, ambientes, níveis, vistas de planta, portas, janelas, mobiliário compatível, materiais e parâmetros.
- Simulação revertida antes da aplicação, resumo de impactos e confirmação. Aplicação em um grupo de transações que pode ser desfeito no Revit.
- Proteções contra projeto errado, proposta desatualizada, código arbitrário da IA e carregamento de famílias não autorizadas.

## Primeiro uso

1. Salve seus projetos e reinicie o Revit quando for conveniente.
2. Acesse **ChatGPT → BIM Copiloto**.
3. Na lateral, role até **Conexão**. Escolha **OpenAI · Responses** ou **Google · Gemini 3.8 Flash**. Digite a chave correspondente e clique em **Salvar chave**. Não envie a chave no chat.
4. Confira o nome do projeto e clique em **Atualizar contexto**.
5. Escreva sua pergunta e use **Enviar**. Para mudar o modelo, use **Propor edição** e depois a aba **Revisão e edição**.

A chave anteriormente enviada no chat deve ser revogada na plataforma correspondente. Este pacote não inclui chave.
Com OpenAI, o campo **Modelo da API** é editável e o padrão é `gpt-5`. Com Gemini, o modelo é fixado em `gemini-3.8-flash`. Cada modelo precisa estar disponível no projeto correspondente.
OpenAI e Google têm chaves, faturamento, limites e políticas separados; a assinatura do ChatGPT não substitui o faturamento da API OpenAI, e uma chave OpenAI não funciona no Gemini.
A API tem cobrança própria; a assinatura do ChatGPT não substitui os requisitos de acesso/faturamento da API.
Para o modelo padrão GPT-5, a conversa usa esforço de raciocínio baixo; no Gemini 3.8 Flash a conversa e as propostas usam nível de pensamento baixo. Isso reduz processamento, mas não garante resposta instantânea.

Se a resposta for interrompida, leia o motivo na faixa inferior. O plugin distingue limite de tokens, saldo, limite de requisições, modelo indisponível e falha do serviço, sem mostrar mensagens brutas que possam expor dados. Não há repetição automática de chamadas pagas. Para um primeiro teste simples, desmarque **Incluir dados do projeto** e envie uma pergunta curta; depois ative o contexto.

## Criar uma planta a partir de imagem

1. Abra **Planta por imagem → Escolher planta**. Prefira planta ortogonal legível, sem perspectiva.
2. Use **Reconhecer desenho**. A imagem será enviada à OpenAI; aguarde a análise.
3. Confira os traços e a tabela. Desmarque erros, ajuste coordenadas e paredes hospedeiras; você também pode **Adicionar parede (2 pontos)** sem IA.
4. Clique em **Marcar escala (2 pontos)** e selecione as extremidades de uma distância conhecida. Informe a medida real em **milímetros**. O valor inicial é apenas um campo a preencher, não uma escala detectada.
5. Selecione nível e tipos existentes de parede, porta e janela. Confira altura, origem e rotação. O primeiro ponto de escala define a origem indicada.
6. Use **Preparar modelagem no Revit**, leia a proposta e clique em **Simular e revisar impactos**.
7. Somente após conferir o resultado, clique em **Aplicar no Revit** e confirme.

A escala não pode ser determinada com precisão só pelos pixels. Reconhecimento visual pode omitir ou inventar traços e exige revisão.
A imagem define eixos e posições aproximadas; espessuras, larguras das portas/janelas e outros atributos vêm dos tipos selecionados. O peitoril inicial de janelas é 1000 mm e pode ser ajustado no plano antes de simular.
O reconhecimento não gera automaticamente toda a arquitetura, estrutura ou instalações.

## Editar o projeto por conversa

Exemplos de pedidos: “Explique os parâmetros dos elementos selecionados”, “Proponha mover este móvel 300 mm no eixo X” ou “Crie quatro paredes retas no nível Térreo, com estas coordenadas…”.
Use os nomes e dimensões reais do projeto. Se faltarem dados ou famílias, o assistente deve pedir esclarecimentos.
Carregue arquivos locais autorizados pelo botão **Família .rfa**. Modelos do SketchUp não são famílias Revit nativas.

A proposta JSON fica visível e editável. Revise especialmente exclusões, parâmetros de tipo e camadas, pois podem afetar dependências e várias instâncias.
A simulação executa e reverte alterações no Revit; não é uma renderização. O arquivo .rvt nunca é salvo automaticamente.

## Dados e segurança

- A chave salva vai para o Gerenciador de Credenciais do Windows, em entradas separadas por provedor. Alternativamente, o plugin lê `OPENAI_API_KEY` ou `GEMINI_API_KEY`; a chave salva do provedor escolhido tem prioridade.
- Não há login por senha do ChatGPT, acesso aos cookies ou chave embutida.
- Atualizar contexto e capturar vista são operações locais. **Enviar**, **Propor edição** e **Reconhecer desenho** fazem requisições à OpenAI.
- Desmarque **Incluir dados do projeto** para conversar sem enviar seu resumo; imagens anexadas ainda são enviadas quando você solicita análise.
- O resumo seleciona até 800 elementos, priorizando selecionados, e avisa quando há omissões. Não é todo o banco BIM nem toda a geometria.
- Conversa: até 24 mensagens recentes completas e quatro imagens recentes; propostas usam até oito mensagens de contexto. Isso não equivale a memória ilimitada.
- Imagens são redimensionadas até 2048 pixels no maior lado. As imagens do histórico ficam apenas na sessão; ao reabrir, restam as mensagens e nomes dos anexos.
- Histórico e registros de aplicação ficam em `%LOCALAPPDATA%\BIMCopiloto`, sem criptografia própria; podem conter informações do projeto. A chave não vai para esses registros.
- As chamadas usam `store: false`; isso não é promessa de ausência total de retenção pelo provedor. Consulte suas políticas e autorizações antes de enviar projetos de clientes.

## Instalar e compilar

O pacote compilado contém `dist\release\BIMCopiloto.dll`.
Execute `install.ps1` no PowerShell para instalar para o usuário atual. O instalador preserva o manifesto anterior e usa uma pasta identificada pelo hash da DLL, sem sobrescrever a DLL carregada no Revit.
Depois, reinicie o Revit por conta própria. Se houver pedido do Revit sobre confiança/carregamento do add-in, cabe a você avaliá-lo.

Para recompilar, é necessário .NET 10 SDK e os assemblies da instalação de Revit 2027:
```powershell
.\build.ps1
.\install.ps1
```

O SDK pode ser informado com `-DotnetPath`; o caminho do Revit com `-RevitInstallDir`.
Para testes locais: `dotnet run --project tests/Tests.csproj -c Release`.
O instalador não modifica nem fecha o arquivo .rvt aberto.
Se a política do Windows bloquear scripts, não altere configurações globais sem orientação. Nesta instalação, foi autorizada execução restrita ao processo do instalador.

## Limitações nesta versão

Não inclui exportação .skp, busca/download automático de móveis do 3D Warehouse, renderização fotorrealista, geração de imagens, voz, projeto executivo completo, dimensionamento estrutural ou garantia de conformidade normativa.
Mobiliário depende de famílias Revit fornecidas e compatíveis. Importar uma imagem não transforma automaticamente qualquer objeto desenhado em família BIM.
Não há “acesso total” irrestrito: consulte EDITING.md para as ações permitidas e TESTING.md para o que foi testado.

## Referências da integração

A implementação segue a documentação oficial da OpenAI para [respostas progressivas](https://developers.openai.com/api/docs/guides/streaming-responses), [análise de imagens](https://developers.openai.com/api/docs/guides/images-vision) e [saídas estruturadas](https://developers.openai.com/api/docs/guides/structured-outputs), e a documentação do Google para [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash), [imagens](https://ai.google.dev/gemini-api/docs/image-understanding) e [saídas estruturadas com streaming](https://ai.google.dev/gemini-api/docs/structured-output).
O ajuste de conversa e os diagnósticos seguem as opções de [GPT-5](https://developers.openai.com/api/docs/models/gpt-5) e o tratamento de [respostas incompletas](https://developers.openai.com/api/docs/guides/reasoning).
