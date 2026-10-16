# Validação · 29/09/2026

## Ambiente

Windows, Revit 2027.3, .NET SDK 10, WPF. Projeto ativo na conferência final: Casa Bispo2.
Compilação concluída sem erros. Há dois avisos de conflito transitivo de versão de Microsoft.VisualBasic oriundos das referências Autodesk; o carregamento e os testes locais funcionaram neste ambiente.

## 26 testes automatizados locais aprovados

Executados em tests/Tests.csproj, sem rede nem chave real:

- Escala em imagem não quadrada, inversão de Y, origem e rotação.
- Rejeição de referência de escala nula, coordenadas fora da imagem e abertura sem parede.
- Ordenação de paredes/hospedeiros, IDs de 64 bits e rejeição de ações/JSON desconhecidos.
- Rejeição de portas sem parede e deslocamentos extremos.
- Respostas progressivas por eventos SSE, cancelamento e rejeição de fluxo truncado.
- Configuração store=false, stream=true e anexos input_image.
- Raciocínio baixo somente para a conversa no modelo GPT-5 configurado.
- Ausência dessa configuração em modelos não reconhecidos.
- Diagnóstico de limite de tokens e saldo; mensagens desconhecidas e autenticação sem vazamento de resposta bruta.
- Gemini: SSE incremental, modelo/store=false, autenticação por cabeçalho separado, imagem inline e plano estruturado.

## Cinco cenários de integração aprovados dentro do Revit

Executados em projeto métrico temporário, fechado sem salvar. Nenhuma edição aplicada ao projeto ativo:

1. Identificador de estado estável e preservado após simulação revertida.
2. Simulação de quatro paredes, piso, ambiente, material e vista, retornando à contagem inicial de 2407 elementos.
3. Aplicação dessas ações no documento temporário, com quatro paredes confirmadas.
4. Falha na segunda ação revertendo também a primeira.
5. Prévia de parâmetro, movimento e exclusão com contagem de dependências; o aviso de ambiente não fechado foi registrado.

tests/RevitSmokeTest.cs.txt contém o corpo C# para repetição por um executor autorizado da API dentro do Revit. Ajuste apenas o caminho da DLL. Não execute como macro sobre seu projeto de produção; o teste deve continuar criando seu próprio documento temporário.

## Conferência do projeto e interface

- Janela WPF aberta dentro do Revit; layout adaptado à altura da tela, painel lateral rolável e mensagens sem corte horizontal.
- Identidade nativa do documento validada mesmo com wrappers .NET diferentes.
- Contexto do projeto Casa Bispo2 coletado: resumo de 800 elementos e 21 ambientes, com aviso de 1117 elementos omitidos.
- Correção verificada para famílias cuja rotação escalar não é exposta pela API: a comparação de estado utiliza sua transformação 3D.
- A interface mostrou chave salva pelo próprio usuário e uma tentativa manual de conversa interrompida. A chave não foi lida, copiada ou testada pelos testes automatizados.
- Acrescentados diagnósticos específicos de interrupção e esforço reduzido de conversa, mas a resposta real após essa última mudança ainda precisa de validação pelo usuário.

## Não validado ponta a ponta

Reconhecimento de uma imagem real por cada API, conversa paga após a última atualização, colocação de todas as famílias possíveis, todas as operações de edição e inicialização do botão após reiniciar o Revit.
Os testes não garantem exatidão de reconhecimento visual, projeto executivo correto, compatibilidade com outros anos do Revit nem ausência de falhas em modelos grandes.
O instalador copia a DLL e verifica hash; o carregamento definitivo depende de reiniciar o Revit. Não reiniciamos nem salvamos o projeto do usuário.
