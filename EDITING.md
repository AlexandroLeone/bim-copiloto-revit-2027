# Edição assistida e limites

O plugin usa ações validadas, não executa código C# ou comandos arbitrários escritos pela IA.
Limite de 300 ações por plano. Coordenadas de criação e deslocamentos em milímetros; rotação em graus; Z é deslocamento acima do nível indicado.

## Operações

| Operação | Efeito |
| --- | --- |
| set_parameter | Parâmetro editável de instância ou tipo |
| move / rotate | Deslocar ou girar um elemento |
| delete | Excluir elemento e dependências determinadas pelo Revit |
| create_material | Criar material e cor |
| set_material | Atribuir material a um parâmetro de material |
| set_layer_material | Alterar material de camada de um tipo de parede ou piso |
| change_type | Trocar por um tipo compatível |
| load_family | Carregar .rfa local escolhido nesta sessão |
| place_family / place_furniture | Inserir família pontual compatível com nível |
| place_door / place_window | Inserir família em parede hospedeira |
| create_wall | Criar parede básica reta |
| create_floor | Criar piso com contorno poligonal |
| create_room | Criar ambiente delimitado |
| create_level | Criar nível |
| create_plan | Criar vista de planta |

## Revisão e execução

1. A proposta é associada ao documento e a um estado do modelo.
2. A simulação executa o plano em um grupo de transações e compara elementos antes/depois para contabilizar dependências.
3. O grupo é revertido. O resumo mostra inclusões, alterações, exclusões e avisos.
4. A aplicação exige plano idêntico ao simulado, estado ainda válido e confirmação.
5. Falhas em qualquer ação revertem o grupo. Uma aplicação concluída fica agrupada no Desfazer do Revit.

O resumo de dependências não é uma verificação completa de colisões, normas ou segurança construtiva.
Uma alteração posterior no projeto invalida a proposta; gere uma nova. Modelos grandes podem tornar a coleta e a comparação de estado mais lentas.
Uma cópia de arquivo pode compartilhar o identificador de criação: a proteção também compara a identidade nativa do documento.

## Famílias e materiais

As famílias .rfa devem ser escolhidas pelo usuário na sessão. Não há download automático nem sobrescrita silenciosa de famílias já carregadas.
A inserção genérica suporta famílias pontuais baseadas em nível; famílias por face, adaptativas, estruturais especiais, curvas e outros tipos podem ser recusados.
Portas e janelas exigem parede e tipo compatíveis. A posição deve estar próxima da parede e dentro de seu comprimento.
Cores e atribuição de materiais não incluem criação automática de texturas PBR ou renderização.

## Restrições

Projetos somente leitura, documentos de família, vínculos, elementos fixados ou em grupos e parâmetros somente leitura são protegidos conforme a operação.
Alterações de tipo e camadas podem afetar todas as instâncias do tipo, mesmo quando uma única instância foi selecionada.
Para valores dimensionais em parâmetros, informe unidades explícitas; não há conversão silenciosa para pés.
Worksharing, design options, restrições geométricas e erros do Revit continuam valendo. Não há bypass de permissões.
O botão Parar cancela a solicitação de IA; uma transação do Revit já em execução precisa terminar ou reverter.
