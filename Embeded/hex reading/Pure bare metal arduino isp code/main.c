// =====================================================================
// Pure Bare-Metal C ArduinoISP (For ATmega328P / Arduino Nano)
// =====================================================================

#define F_CPU 16000000UL  // 16MHz Clock Speed
#include <avr/io.h>
#include <util/delay.h>

#define BAUDRATE 19200

// STK500 Протокол එකට අදාළ Commands
#define STK_OK              0x10
#define STK_INSYNC          0x14
#define STK_NOSYNC          0x15

// Pins සෙටප් එක (PORTB සහ PORTD වල)
#define RESET_PIN   PB2  // Arduino Pin 10 (Target Reset)
#define LED_PMODE   PD7  // Arduino Pin 7  (Programming Mode LED)
#define LED_ERR     PD8  // Arduino Pin 8  (Error LED)

// --- UART (Serial) සන්නිවේදනය ---
void uart_init(uint32_t baud) {
    uint16_t ubrr = (F_CPU / (16 * baud)) - 1;
    UBRR0H = (uint8_t)(ubrr >> 8);
    UBRR0L = (uint8_t)ubrr;
    UCSR0B = (1 << RXEN0) | (1 << TXEN0); // RX සහ TX සක්‍රීය කිරීම
    UCSR0C = (1 << UCSZ01) | (1 << UCSZ00); // 8-bit Data Format
}

void uart_write(uint8_t data) {
    while (!(UCSR0A & (1 << UDRE0))); // Buffer එක හිස් වෙනකම් ඉන්නවා
    UDR0 = data;
}

uint8_t uart_read(void) {
    while (!(UCSR0A & (1 << RXC0))); // ඩේටා එකක් එනකම් ඉන්නවා
    return UDR0;
}

// --- SPI (Target එකත් එක්ක කතා කිරීමට) ---
void spi_init(void) {
    // MOSI (PB3) සහ SCK (PB5) Pins Output කිරීම
    DDRB |= (1 << PB3) | (1 << PB5);
    // SPI සක්‍රීය කිරීම, Master Mode, Clock = F_CPU/64
    SPCR = (1 << SPE) | (1 << MSTR) | (1 << SPR1);
}

uint8_t spi_transfer(uint8_t data) {
    SPDR = data;
    while (!(SPSR & (1 << SPIF))); // Hardware එකෙන්ම ඩේටා එක යවනකම් ඉන්නවා
    return SPDR;
}

// --- GPIO සහ Reset පාලනය ---
void gpio_init(void) {
    DDRB |= (1 << RESET_PIN); // Reset Pin එක Output කිරීම
    DDRD |= (1 << LED_PMODE); // Programming LED එක Output කිරීම
    DDRB |= (1 << PB0);        // Error LED (Pin 8 තියෙන්නේ PORTB වල) -> Output කිරීම
}

void set_reset(uint8_t level) {
    if (level) PORTB |= (1 << RESET_PIN);  // HIGH (5V)
    else PORTB &= ~(1 << RESET_PIN);       // LOW (0V)
}

// --- ISP ප්‍රධාන ලොජික් ---
uint8_t pmode = 0;
unsigned int address;
uint8_t buff[256];

void start_pmode(void) {
    spi_init();
    set_reset(1);
    _delay_ms(20);
    set_reset(0); // Target චිප් එක RESET කරනවා Programming Mode එකට ගන්න
    _delay_ms(20);
    
    // Programming Enable Command එක SPI හරහා යැවීම
    spi_transfer(0xAC);
    spi_transfer(0x53);
    spi_transfer(0x00);
    spi_transfer(0x00);
    pmode = 1;
    PORTD |= (1 << LED_PMODE); // LED ඔන් කිරීම
}

void end_pmode(void) {
    // SPI අක්‍රීය කර පින් සාමාන්‍ය තත්ත්වයට පත් කිරීම
    SPCR &= ~(1 << SPE);
    DDRB &= ~((1 << PB3) | (1 << PB5) | (1 << RESET_PIN));
    pmode = 0;
    PORTD &= ~(1 << LED_PMODE);
}

void empty_reply(void) {
    if (uart_read() == 0x20) {
        uart_write(STK_INSYNC);
        uart_write(STK_OK);
    } else {
        uart_write(STK_NOSYNC);
    }
}

// --- ප්‍රධාන Function එක ---
int main(void) {
    gpio_init();
    uart_init(BAUDRATE);
    
    while (1) {
        uint8_t ch = uart_read(); // PC එකෙන් (avrdude) එන Command එක කියවීම
        
        switch (ch) {
            case 0x30: // Get Sync
                empty_reply(); 
                break;
            case 0x50: // Enter Programming Mode
                start_pmode(); 
                empty_reply(); 
                break;
            case 0x51: // Leave Programming Mode
                end_pmode(); 
                empty_reply(); 
                break;
            case 0x55: // Set Address
                address = uart_read() | (uart_read() << 8);
                empty_reply();
                break;
            case 0x74: // Read Page (චිප් එකෙන් කෝඩ් එක එළියට ඇදීම)
                {
                    uint8_t high_byte = uart_read();
                    uint8_t low_byte = uart_read();
                    int length = (high_byte << 8) | low_byte;
                    uint8_t memtype = uart_read();
                    
                    if (uart_read() == 0x20) {
                        uart_write(STK_INSYNC);
                        if (memtype == 'F') { // Flash Memory එක නම් කියවන්නේ
                            for (int i = 0; i < length; i += 2) {
                                uart_write(spi_transfer(0x20)); // Low Byte එක කියවීම
                                uart_write(spi_transfer(0x28)); // High Byte එක කියවීම
                            }
                        }
                        uart_write(STK_OK);
                    }
                }
                break;
            default:
                if (uart_read() == 0x20) {
                    uart_write(STK_NOSYNC);
                }
                break;
        }
    }
    return 0;
}