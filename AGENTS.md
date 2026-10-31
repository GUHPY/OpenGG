# AGENTS.md - Protocolo Obrigatório de Commits & Desenvolvimento
**Projeto:** `OpenGG`  
**Escopo Principal:** `Gaming Tools, Telemetry, Overlay HUD, Performance`  
**Descrição:** Ferramenta open-source de overlay e telemetria para jogos.

> **DIRETIVA CRÍTICA PARA TODOS OS AGENTES DE IA (Antigravity, Cursor, Claude Code, Codex, Windsurf, OpenHands, etc.):**
> Este repositório adota um fluxo de trabalho estrito de **micro-commits atômicos e hipergranulares**.
> **NUNCA** acumule alterações de múltiplos arquivos, camadas ou módulos em um único commit geral.
> Cada pequena mudança deve se tornar seu próprio commit independente e semântico.

---

## 1. Princípio Fundamental: Micro-Commits Atômicos
Toda e qualquer alteração de código DEVE ser fatiada na menor unidade lógica:
- **Tamanho Máximo por Commit**: 1 a 3 arquivos estritamente relacionados à mesma função/componente.
- **Separação por Camadas**: Se uma tarefa envolve UI, contratos/tipos, backend e testes, faça no mínimo 4 commits separados:
  1. `feat(contracts): update schemas and data contracts`
  2. `feat(backend): implement handler and business logic`
  3. `feat(ui): add visual components and user interactions`
  4. `test(integration): add unit and integration test assertions`
- **Refatorações Isoladas**: Separe refatorações de código, formatação e novas funcionalidades em commits distintos.

---

## 2. Formato Padronizado de Mensagens (Conventional Commits)
Siga o padrão:
`<tipo>(<escopo>): <descrição clara e objetiva>`

### Tipos Válidos:
- `feat`: Nova funcionalidade, componente, endpoint ou recurso.
- `fix`: Correção de bug, tratamento de exceção ou ajuste de comportamento.
- `perf`: Otimização de desempenho, memória, zero-copy ou renderização.
- `refactor`: Refatoração estrutural sem alteração funcional externa.
- `docs`: Documentação técnica, specs de arquitetura, guias de módulo.
- `test`: Testes unitários, de regressão ou matriz de validação.
- `chore`: Atualização de dependências, scripts de build ou ponteiros de submódulo.

### Exemplos Válidos para OpenGG:
- `feat(gaming tools): add initial implementation`
- `fix(gaming tools): resolve edge case handling`
- `docs(gaming tools): update technical architecture spec`

---

## 3. Regra do Commit Duplo (Sincronização com DevLogs)
Sempre que você implementar uma arquitetura nova, refatoração estrutural ou otimização importante:
1. Comite as alterações granulares no repositório local deste projeto.
2. Registre uma entrada técnica correspondente no repositório de histórico técnico:
   `X:\gugac\Desktop\Projects\DevLogs\logs\<projeto>\<submodulo>.md`
3. Comite no `DevLogs` detalhando: decisões técnicas tomadas, benchmarks e validações.

---

## 4. Identidade de Git Obrigatória
Antes de commitar, certifique-se de que o autor está configurado como:
```bash
git config user.name "GUHPY"
git config user.email "gugadiamon@gmail.com"
```
Todos os commits devem pertencer à conta oficial para contabilização no perfil do GitHub.

---

## 5. Higiene do Repositório & Árvore Limpa
- **Árvore de Trabalho Limpa**: Nunca encerre um turno ou tarefa deixando arquivos modificados ou `untracked` pendentes no `git status`.
- **Merge & Push Contínuos**: Mantenha a branch `main` atualizada e envie as alterações para o remoto (`git push origin <branch>`).
- **Submódulos**: Se você alterar algo dentro de uma subpasta que seja submódulo git, lembre-se de entrar na pasta pai e commitar a atualização do ponteiro do submódulo.

---

## 6. Checklist de Execução para Agentes
- [ ] Alteração feita em escopo reduzido (1 a 3 arquivos).
- [ ] `git status` checado para garantir que apenas os arquivos daquele escopo estão sendo adicionados.
- [ ] `git add <arquivos>` feito seletivamente.
- [ ] `git commit -m "<tipo>(<escopo>): <mensagem>"` executado com mensagem descritiva.
- [ ] Repetir o processo para os próximos arquivos alterados.
- [ ] `git push origin <branch>` executado com sucesso.

---

## 7. Fluxo Obrigatório de Documentação por Etapa

Toda tarefa que não seja trivial (mais de um arquivo ou mais de um passo) segue este ciclo. **Nunca substitua a documentação existente do projeto: acrescente.**

1. **Plano por fases**: antes de codar, escreva ou atualize o plano na pasta de docs do projeto (ex.: `Docs/<área>/ativo/<tarefa>/<nome>-PLANO.md`). Divida em fases F1…Fn. Cada fase tem: o escopo, os arquivos tocados, a origem (de onde vem a referência) e o **aceite** (o que prova que ficou pronto).
2. **Execução fase a fase**: implemente uma fase por vez, na ordem. Não peça permissão entre fases quando o plano já foi aprovado.
3. **Reverificação ao fim de CADA fase**, antes de seguir:
   - typecheck/compilação do que foi tocado;
   - testes unitários da área (e um teste novo para lógica não trivial);
   - os greps/checagens de aceite que o plano definir;
   - falhas que já existiam antes da tarefa são registradas como pré-existentes, nunca escondidas.
4. **Checklist por fase**: ao fim de cada fase, acrescente uma seção ao checklist acumulado do projeto (ex.: `Docs/<área>/checklists.md`), no formato:
   - `## <Plano> · Fn — <título> (<data>)`;
   - itens `- [ ]` que o usuário consegue testar na tela, escritos como passo + resultado esperado;
   - linhas `- Fora: …` e `- Nota: …` para o que ficou de fora ou mudou, sempre com o motivo.
   Preserve o fim de linha (CRLF/LF) do arquivo.
5. **Documentação por etapa**: atualize as specs, os READMEs ou os guias que a fase deixou desatualizados. Simplificações com teto conhecido levam um comentário no código dizendo o teto e o caminho de upgrade.
6. **Fechamento**: no fim do plano, marque cada item do documento de origem (comparativo, issue, spec) como **feito (Fn)**, **Fora (motivo)** ou **aguardando o usuário**. Rode de novo todas as checagens de aceite.
7. **DevLogs**: registre em `X:\gugac\Desktop\Projects\DevLogs\logs\<projeto>\<submodulo>.md` as decisões, o que ficou fora e a validação (seção 3).

### Commits: o máximo possível de fragmentos
- Prefira **sempre mais commits**: um por arquivo ou por par de arquivos acoplados, separados por camada (contracts → backend → frontend → testes → docs).
- Testes, docs, checklists e DevLogs vão em commits próprios, nunca misturados com código.
- Commite ao fim de cada fase (não acumule fases), faça o push e deixe o `git status` limpo.

---

## 8. Commit Duplo SEMPRE + Credencial Única

### Todo commit tem espelho no DevLogs
- **Não é só para arquitetura**: TODO commit feito neste projeto tem um commit correspondente no repositório `X:\gugac\Desktop\Projects\DevLogs`.
- Fragmente o trabalho na maior quantidade de commits possível. **Cada fragmento** leva no DevLogs:
  - a sua própria entrada de documentação em `logs/<projeto>/<submodulo>.md` (o que mudou, por quê, os arquivos e o hash do commit de origem);
  - o seu próprio checklist (`- [ ]` passo + resultado esperado, e `Fora:`/`Nota:` com o motivo).
- Ordem: commit no projeto → entrada + checklist no DevLogs → commit no DevLogs (`docs(<projeto>): <mesmo assunto do commit de origem>`) → push dos dois.

### Credencial: somente a do dono
- Todo commit é feito **somente** com a identidade `GUHPY <gugadiamon@gmail.com>` (seção 4).
- **Nunca** use a identidade de agente/IA (Claude, Codex, Cursor, Copilot etc.): sem `Co-Authored-By`, sem trailer "Generated with" e sem autor ou committer de bot, em commits e PRs.

---

## 9. Todo Commit: Push + Merge na `main`
- Todo commit é seguido de `git push origin <branch>` na hora, sem acumular commits locais.
- Se o trabalho está numa branch que não é a `main`, faça o merge na `main` logo depois (`git checkout main && git merge --no-ff <branch> && git push origin main`) e volte para a branch. A `main` nunca fica para trás.
- O espelho no DevLogs (seção 8) segue a mesma regra: commit, push e `main` atualizada.
- Conflito no merge: resolva sem descartar o trabalho de ninguém. Se não der para resolver com segurança, pare e avise o dono antes de forçar qualquer coisa.
