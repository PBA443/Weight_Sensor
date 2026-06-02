#define F_CPU 16000000UL
#include <util/delay.h>
#include <stdint.h>
#include <avr/sleep.h>
#include <avr/interrupt.h> // Required for bare-metal ISR handling

// --- Bare-Metal Register Mappings ---
#define MY_UBRR0H  (*(volatile uint8_t *)(0xC5))
#define MY_UBRR0L  (*(volatile uint8_t *)(0xC4))
#define MY_UCSR0A  (*(volatile uint8_t *)(0xC0))
#define MY_UCSR0B  (*(volatile uint8_t *)(0xC1))
#define MY_UCSR0C  (*(volatile uint8_t *)(0xC2))
#define MY_UDR0    (*(volatile uint8_t *)(0xC6))

#define PORTD      (*(volatile uint8_t *)(0x2B))
#define DDRD       (*(volatile uint8_t *)(0x2A))
#define PIND       (*(volatile uint8_t *)(0x29))

// Interrupt Configuration Registers
#define MY_PCICR   (*(volatile uint8_t *)(0x68)) // Pin Change Interrupt Control Register
#define MY_PCMSK2  (*(volatile uint8_t *)(0x6D)) // Pin Change Mask Register 2

#define UDRE0  5
#define TXEN0  3
#define PORTD3 3 // HX711 SCK (Output)
#define PIND2  2 // HX711 DT  (Input / PCINT18)

typedef struct __attribute__((packed)) {
    uint8_t header;      // 0xAA
    uint32_t raw_weight; // 4 bytes
    uint8_t checksum;    // 1 byte
} ScalePacket;

// Global volatile variable to share data safely between ISR and main loops
volatile int32_t global_raw_weight = 0;
volatile uint8_t data_ready_flag = 0;

void uart_init(void) {
    MY_UBRR0H = 0;
    MY_UBRR0L = 104; // 9600 Baud at 16MHz
    MY_UCSR0B = (1 << TXEN0);
    MY_UCSR0C = (1 << 2) | (1 << 1); // 8N1 framing
}

void uart_send_byte(uint8_t b) {
    while (!(MY_UCSR0A & (1 << UDRE0)));
    MY_UDR0 = b;
}

void uart_send_buffer(uint8_t *buffer, uint8_t size) {
    for (uint8_t i = 0; i < size; i++) {
        uart_send_byte(buffer[i]);
    }
}

// --- THE INTERRUPT SERVICE ROUTINE (ISR) ---
// PCINT2_vect handles pin changes on PORTD pins (PCINT16 to PCINT23)
ISR(PCINT2_vect) {
    // Check if PD2 actually went LOW (HX711 signals data is ready)
    if (!(PIND & (1 << PIND2))) {
        
        // Disable pin change interrupts temporarily so our clock pulses don't trigger a recursive interrupt loop
        MY_PCICR &= ~(1 << 2); 
        
        int32_t count = 0;
        _delay_us(5); // Microscopic stabilization pause

        // Bit-bang out the 24 bits exactly like before
        for (uint8_t i = 0; i < 24; i++) {
            PORTD |= (1 << PORTD3);   _delay_us(2);
            count = count << 1;
            PORTD &= ~(1 << PORTD3);  _delay_us(2);
            
            if (PIND & (1 << PIND2)) {
                count++;
            }
        }
        
        // 25th pulse for Channel A, 128 gain
        PORTD |= (1 << PORTD3);  _delay_us(1);
        PORTD &= ~(1 << PORTD3); _delay_us(1);

        // Sign extension
        if (count & 0x800000) {
            count |= 0xFF000000;
        }

        global_raw_weight = count;
        data_ready_flag = 1; // Alert the system a new transmission is processed

        // Clear any pending interrupt flags that might have accidentally tripped during our read execution
        (*(volatile uint8_t *)(0x3B)) |= (1 << 2); // PCIFR clear flag
        
        // Re-enable Pin Change Interrupts for the next conversion sample
        MY_PCICR |= (1 << 2);
    }
}

int main(void) {
    // Hardware Pins
    DDRD &= ~(1 << PIND2);   // PD2 Input (DT)
    DDRD |= (1 << PORTD3);   // PD3 Output (SCK)
    PORTD &= ~(1 << PORTD3); // SCK starts LOW

    uart_init();

    // --- INTERRUPT SETUP ---
    MY_PCICR |= (1 << 2);    // Enable Pin Change Interrupt group 2 (PCIE2 handles PORTD)
    MY_PCMSK2 |= (1 << 2);   // Turn on interrupt capability specifically for PD2 (PCINT18)

    sei(); // Global Interrupt Enable (Sets the 'I' bit in the status register)
    //set_sleep_mode(SLEEP_MODE_IDLE);
    while (1) {
        // Look at how beautiful this is. The CPU does absolutely nothing 
        // until the interrupt safely handles data collection in the background!
        if (data_ready_flag) {
            data_ready_flag = 0; // Clear flag

            // uint8_t b3 = (global_raw_weight >> 24) & 0xFF;
            // uint8_t b2 = (global_raw_weight >> 16) & 0xFF;
            // uint8_t b1 = (global_raw_weight >> 8)  & 0xFF;
            // uint8_t b0 = global_raw_weight & 0xFF;
            
            // uint8_t checksum = b3 + b2 + b1 + b0;

            // uart_send_byte(0xAA);     // 1. Header
            // uart_send_byte(b3);       // 2. Data
            // uart_send_byte(b2);       // 3. Data
            // uart_send_byte(b1);       // 4. Data
            // uart_send_byte(b0);       // 5. Data
            // uart_send_byte(checksum); // 6. Checksum (simple sum of data bytes for integrity verification)
            
            ScalePacket packet;
            packet.header = 0xAA;
            packet.raw_weight = __builtin_bswap32(global_raw_weight);
            uint8_t *ptr = (uint8_t *)&packet.raw_weight;
            packet.checksum = ptr[0] + ptr[1] + ptr[2] + ptr[3];
            uart_send_buffer((uint8_t *)&packet, sizeof(ScalePacket));
        }
        //sleep_mode();
    }
    return 0;
}