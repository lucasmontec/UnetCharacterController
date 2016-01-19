# UnetCharacterController

## Short description
A Unity UNET based authoritative character controller with prediction, reconciliation and interpolation.

### ptBR
Um controlador de personagem para Unity feito com UNET autoritativo com predição, reconciliação e interpolação.

## Hello!
Hi there! I'm a developer in a small game dev team in Brazil and while coding a Networked Character Controller I realized UNET
didn't provided nothing authoritative and server side. Server side is really important to fight hackers in almost any fast paced
game. I'm developing a fast paced FPS. Well, long story short I decided to create this server side controller and share here so we
can improve it.

### ptBR
Olá! Sou um desenvolvedor de um pequeno time de desenvolvimento de jogos no Brasil e enquanto programava um controlador de personagem
multijogador eu percebi que a UNET não oferecia nada igual por padrão. Não havia uma opção com posicionamento server-side autoritativo.
Colocar a posição no servidor é muito importante na luta contra os hackers em quase qualquer jogo rápido. Estou desenvolvendo um FPS
rapído. Bom, resumindo eu decidi criar esse controlador server-side baseado no padrão da unity e dividir aqui para melhorar ele.

## Getting started
Well, before you can put your hands to work you need to understand a bit of theory. I'll give you some study material so that you can better understand what the code ~~(is supposed to do)~~ does. You'll need to be confortable with these terms:

* Prediction
* Reconciliation
* Interpolation
* Dummy (Non local authority) clients
* Lag compensation

It is also quite important to have some understanding of basic networking. Knowing what are the main differences between TCP and UDP, some quality of service theory too. But this is extra.
Our controller is based on this articles:

* [Valve's article](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)
* [Gambetta's article](http://gabrielgambetta.com/)
* [Unity LLAPI](http://www.robotmonkeybrain.com/good-enough-guide-to-unitys-unet-transport-layer-llapi/)
* [Unity HLAPI](http://docs.unity3d.com/Manual/UNetUsingHLAPI.html)

### ptBR
Bom, antes de começar a bater a cabeça no teclado será necessário que você conheça um pouco de teoria. Vou passar um material de estudo aqui para que você entenda melhor o que o código ~~~(deve fazer)~~ faz. Você precisa estar confortável com a seguinte terminologia:

* Predição
* Reconciliação
* Interpolação
* Clientes (não locais) Dummy
* Compensação de lag

É também de grande importância ter algum conhecimento basico sobre redes. Saber a diferença principal entre TCP e UDP, algum conhecimento sobre a teoria de qualidade de serviço também. Mas isso é extra.
Nosso controlador é baseado nos seguintes artigos:

* [Artigo de multijogador da Valve](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)
* [Artigo sobre jogos fast paced do Gambetta](http://gabrielgambetta.com/)
* [Unity LLAPI](http://www.robotmonkeybrain.com/good-enough-guide-to-unitys-unet-transport-layer-llapi/)
* [Unity HLAPI](http://docs.unity3d.com/Manual/UNetUsingHLAPI.html)
