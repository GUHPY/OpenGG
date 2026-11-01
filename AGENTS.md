# AGENTS.md - Protocolo Obrigatório de Commits & Desenvolvimento
**Projeto:** `OpenGG`  
**Escopo Principal:** `Gaming Tools, Telemetry, Overlay HUD, Performance`  
**Descrição:** Ferramenta open-source de overlay e telemetria para jogos.

> **DIRETIVA CRÍTICA PARA TODOS OS AGENTES DE IA (Antigravity, Cursor, Claude Code, Codex, Windsurf, OpenHands, etc.):**
> Commits são feitos **no fim da tarefa**, não em tempo real. Uma tarefa (ex.: plano F1…F10) gera
> **poucos commits semânticos** (em geral 1 por fase ou por camada), e o push é feito **uma vez, no final**.
> Meta: uma tarefa inteira cabe em ~3 a 10 commits. Nunca um commit por arquivo.

---

## 1. Princípio Fundamental: Commits por Tarefa, no Final
- **Quando commitar**: só ao terminar a tarefa inteira (todas as fases F1…Fn). Durante o trabalho, nada de commit/push a cada arquivo.
- **Tamanho**: um commit agrupa uma unidade lógica completa (uma fase, uma feature, uma camada). Pode ter quantos arquivos ela precisar.
- **Separação**: separe só o que tem sentido separado: código, testes e docs/checklists podem ir juntos se pertencem à mesma fase. Refatoração grande e feature nova ficam em commits distintos.
- **Teto**: uma tarefa típica = 3 a 10 commits. Mais que isso, junte.
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
1. Ao fim da tarefa, comite as alterações no repositório local deste projeto.
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
- [ ] Tarefa inteira concluída e verificada (typecheck/testes) antes de commitar.
- [ ] `git status` checado; arquivos agrupados em poucos commits lógicos (fase/feature).
- [ ] `git commit -m "<tipo>(<escopo>): <mensagem>"` com mensagem descritiva.
- [ ] Um único `git push origin <branch>` no fim da tarefa.
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

### Commits: poucos e no final
- Commite **ao fim da tarefa**, agrupando por fase ou por feature (não por arquivo).
- Testes, docs e checklists podem ir no commit da fase a que pertencem.
- Push único no final; `git status` limpo ao encerrar.
---

## 8. Commit Duplo + Credencial Única

### Espelho no DevLogs: um por tarefa
- Ao **terminar a tarefa**, registre **uma** entrada em `X:\gugac\Desktop\Projects\DevLogs\logs\<projeto>\<submodulo>.md` cobrindo a tarefa toda (o que mudou, por quê, hashes dos commits, checklist `- [ ]` e `Fora:`/`Nota:`).
- **Um** commit no DevLogs por tarefa (`docs(<projeto>): <resumo da tarefa>`), não um por commit do projeto.
- Ordem: commits do projeto → entrada no DevLogs → commit no DevLogs → push dos dois, uma vez.

### Credencial: somente a do dono
- Todo commit é feito **somente** com a identidade `GUHPY <gugadiamon@gmail.com>` (seção 4).
- **Nunca** use a identidade de agente/IA (Claude, Codex, Cursor, Copilot etc.): sem `Co-Authored-By`, sem trailer "Generated with" e sem autor ou committer de bot, em commits e PRs.
---

## 9. Push + Merge na `main` no Fim da Tarefa
- Ao terminar a tarefa, um único `git push origin <branch>`.
- Se o trabalho está numa branch que não é a `main`, faça o merge na `main` em seguida (`git checkout main && git merge --no-ff <branch> && git push origin main`) e volte para a branch.
- O espelho no DevLogs segue a mesma regra: um commit e um push por tarefa.
- Conflito no merge: resolva sem descartar o trabalho de ninguém. Se não der para resolver com segurança, pare e avise o dono antes de forçar qualquer coisa.