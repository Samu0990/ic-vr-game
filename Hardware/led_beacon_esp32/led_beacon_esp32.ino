// Farol de LED do simulador de transplante — ESP32
//
// Recebe o estado da cirurgia por UDP e acende o LED. O jogo roda no Quest sozinho, sem PC e sem
// porta serial, então a rede é o único caminho.
//
// Protocolo, uma linha por pacote:
//
//     VRS:ESTADO:urgencia
//
// ESTADO   IDLE | RUN | WARN | ERR | OK | FAIL
// urgencia 0 a 100, quanto do relógio já passou
//
// Exemplo: VRS:WARN:82
//
// -----------------------------------------------------------------------------------------
// DUAS PROTEÇÕES QUE EXISTEM POR SER UM ESTANDE
//
// 1. Apagar sozinho. Se parar de chegar pacote por SILENCIO_MS, o LED apaga. Jogo travado,
//    Quest sem bateria ou wi-fi caído não podem deixar um LED vermelho aceso na mesa a tarde
//    inteira, dando a impressão de que o paciente está morrendo.
//
// 2. O jogo reenvia o estado a cada meio segundo mesmo sem mudança. UDP perde pacote, e um
//    "o erro acabou" perdido deixaria o vermelho preso.
// -----------------------------------------------------------------------------------------
//
// LIGAÇÃO
//
//   LED RGB cátodo comum        LED simples
//   R  -> GPIO 25 (resistor)    + -> GPIO 25 (com resistor de 220–330 ohm)
//   G  -> GPIO 26 (resistor)    - -> GND
//   B  -> GPIO 27 (resistor)
//   -  -> GND
//
// Com LED de uma cor só, deixe apenas PINO_R ligado: o brilho já conta a história (apagado,
// aceso fraco, piscando devagar, piscando rápido).
//
// Se o LED for ânodo comum, troque ANODO_COMUM para true.

// ATENÇÃO À VERSÃO DO CORE ESP32
//
// Este sketch usa a API do core 3.x: ledcAttach(pino, freq, bits) e ledcWrite(pino, valor).
// No core 2.x a API é outra — ledcSetup(canal, freq, bits) + ledcAttachPin(pino, canal), e o
// ledcWrite recebe o CANAL, não o pino. Se der erro de compilação em ledcAttach, é isso.
//
// Conferir em: Ferramentas → Placa → Gerenciador de placas → esp32 (Espressif).

#include <WiFi.h>
#include <WiFiUdp.h>
#include <string.h>

// ---------------------------------------------------------------- configuração

// MODO_AP = true  : o ESP32 cria a própria rede. O Quest se conecta nela.
//                   Não depende do wi-fi do evento, que costuma ser fechado ou lotado.
//                   Endereço do ESP32 nesse modo: 192.168.4.1
// MODO_AP = false : o ESP32 entra numa rede existente. Fixe o IP no roteador.
const bool  MODO_AP   = true;
const char* SSID_REDE = "TransplanteVR";
const char* SENHA     = "cirurgia2026";   // mínimo 8 caracteres

const uint16_t PORTA_UDP = 4210;

const int PINO_R = 25;
const int PINO_G = 26;
const int PINO_B = 27;

const bool ANODO_COMUM = false;

// Sem pacote por este tempo, apaga.
const unsigned long SILENCIO_MS = 3000;

// ---------------------------------------------------------------- estado

WiFiUDP udp;
char pacote[64];

char estado[12] = "IDLE";
int    urgencia = 0;
unsigned long ultimoPacote = 0;

void setup() {
  Serial.begin(115200);

  ledcAttach(PINO_R, 5000, 8);
  ledcAttach(PINO_G, 5000, 8);
  ledcAttach(PINO_B, 5000, 8);

  if (MODO_AP) {
    WiFi.softAP(SSID_REDE, SENHA);
    Serial.println();
    Serial.print("Rede criada: ");   Serial.println(SSID_REDE);
    Serial.print("Endereco do ESP32: "); Serial.println(WiFi.softAPIP());
    Serial.println("Configure este endereco no LedBeacon do Unity.");
  } else {
    WiFi.begin(SSID_REDE, SENHA);
    Serial.print("Conectando");
    while (WiFi.status() != WL_CONNECTED) { delay(400); Serial.print("."); }
    Serial.println();
    Serial.print("Endereco do ESP32: "); Serial.println(WiFi.localIP());
  }

  udp.begin(PORTA_UDP);
  Serial.print("Ouvindo UDP na porta "); Serial.println(PORTA_UDP);

  piscarInicio();
}

void loop() {
  receber();
  desenhar();
  delay(10);
}

// ---------------------------------------------------------------- recepção

void receber() {
  int tamanho = udp.parsePacket();
  if (tamanho <= 0) return;

  int lidos = udp.read(pacote, sizeof(pacote) - 1);
  if (lidos <= 0) return;
  pacote[lidos] = '\0';

  // Espera VRS:ESTADO:urgencia — qualquer outra coisa é ignorada em silêncio, para um
  // scanner de rede ou um pacote perdido de outro aparelho não mexer no LED.
  //
  // Análise no buffer, sem a classe String: são dois pacotes por segundo durante um dia
  // inteiro de evento, e String no ESP32 fragmenta a heap até o aparelho travar. Trocar o
  // LED de cor não vale um reinício no meio da apresentação.
  if (strncmp(pacote, "VRS:", 4) != 0) return;

  char* corpo = pacote + 4;
  char* divisor = strchr(corpo, ':');
  if (divisor == NULL) return;

  *divisor = '\0';                       // corta o estado do número
  strncpy(estado, corpo, sizeof(estado) - 1);
  estado[sizeof(estado) - 1] = '\0';
  urgencia = atoi(divisor + 1);

  ultimoPacote = millis();

  Serial.print("estado="); Serial.print(estado);
  Serial.print(" urgencia="); Serial.println(urgencia);
}

// ---------------------------------------------------------------- saída

void desenhar() {
  // Silêncio prolongado: apaga. Ver a proteção 1 no cabeçalho.
  if (millis() - ultimoPacote > SILENCIO_MS) { cor(0, 0, 0); return; }

  unsigned long t = millis();

  if (strcmp(estado, "ERR") == 0) {
    // Pisca rápido em vermelho: é o único momento em que o LED precisa gritar.
    bool aceso = (t / 120) % 2 == 0;
    cor(aceso ? 255 : 0, 0, 0);

  } else if (strcmp(estado, "WARN") == 0) {
    // Âmbar pulsando, e mais rápido quanto menos tempo resta. O ritmo carrega a informação
    // que a cor sozinha não carrega.
    int periodo = map(constrain(urgencia, 75, 100), 75, 100, 600, 160);
    bool aceso = (t / periodo) % 2 == 0;
    cor(aceso ? 255 : 20, aceso ? 90 : 8, 0);

  } else if (strcmp(estado, "RUN") == 0) {
    cor(0, 120, 30);                         // verde calmo: em cirurgia, tudo certo

  } else if (strcmp(estado, "OK") == 0) {
    // Verde respirando: transplante concluído.
    float f = (sin(t / 400.0) + 1.0) / 2.0;
    cor(0, 60 + f * 195, 30 + f * 40);

  } else if (strcmp(estado, "FAIL") == 0) {
    cor(120, 0, 0);                          // vermelho parado: tempo esgotado

  } else {
    // IDLE: azul bem fraco, só para mostrar que o aparelho está vivo.
    cor(0, 0, 18);
  }
}

void cor(int r, int g, int b) {
  if (ANODO_COMUM) { r = 255 - r; g = 255 - g; b = 255 - b; }
  ledcWrite(PINO_R, r);
  ledcWrite(PINO_G, g);
  ledcWrite(PINO_B, b);
}

// Vermelho, verde, azul ao ligar: confirma de olho que os três pinos e o resistor estão certos
// antes de depender do jogo para descobrir.
void piscarInicio() {
  cor(255, 0, 0); delay(250);
  cor(0, 255, 0); delay(250);
  cor(0, 0, 255); delay(250);
  cor(0, 0, 0);
}
