FoxMap Studio — Editor de Mapas WYD
Editor de mapas open source para o jogo With Your Destiny (WYD), desenvolvido em C# com OpenGL e ImGui.
<img width="1606" height="932" alt="Screenshot_7" src="https://github.com/user-attachments/assets/f2b8fa04-52d3-4265-8a17-46a9ea0d434e" />

✨ Funcionalidades

Visualização 3D dos mapas do WYD em tempo real
Edição de terreno, texturas e objetos
Suporte a prefabs e importação de modelos 3D
Exportação de mapas no formato nativo do WYD
Interface moderna com ImGui
Conversor de modelos 3D integrado


🖥️ Requisitos

Windows 10/11 (64-bit)
.NET 8 SDK
Placa de vídeo com suporte a OpenGL 3.3+


🚀 Como compilar

Clone o repositório:

bashgit clone https://github.com/FoxdemoX/WydMapEditor.git
cd WydMapEditor

Abra WydMapEditor.sln no Visual Studio 2022 ou Rider
Compile e rode o projeto WydMapEditor

Ou use o bat incluso:
Compilar.bat
Gera um único FoxMapStudio.exe na pasta dist\.

📁 Estrutura do projeto
src/
  WydMapEditor/   → Projeto principal (editor, UI, OpenGL)
  WydFormats/     → Biblioteca de leitura/escrita dos formatos WYD

🛠️ Tecnologias
BibliotecaUsoOpenTK 4.7Renderização OpenGLImGui.NETInterface do editorAssimpNetImportação de modelos 3D

📄 Licença
Este projeto é open source e livre para uso, modificação e distribuição.

👤 Autor
Desenvolvido por FoxdemoX
GitHub: @FoxdemoX
