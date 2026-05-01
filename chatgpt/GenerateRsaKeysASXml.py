from Crypto.PublicKey import RSA
import base64

def public_key_to_xml(key):
    modulus = base64.b64encode(key.n.to_bytes((key.n.bit_length() + 7) // 8, 'big')).decode('utf-8')
    exponent = base64.b64encode(key.e.to_bytes((key.e.bit_length() + 7) // 8, 'big')).decode('utf-8')
    return f"<RSAKeyValue><Modulus>{modulus}</Modulus><Exponent>{exponent}</Exponent></RSAKeyValue>"

def private_key_to_xml(key):
    modulus_length = (key.n.bit_length() + 7) // 8
    half_modulus_length = (modulus_length + 1) // 2
    modulus   = base64.b64encode(key.n.to_bytes(modulus_length, 'big')).decode('utf-8')
    exponent  = base64.b64encode(key.e.to_bytes((key.e.bit_length() + 7) // 8, 'big')).decode('utf-8')
    p         = base64.b64encode(key.p.to_bytes(half_modulus_length, 'big')).decode('utf-8')
    q         = base64.b64encode(key.q.to_bytes(half_modulus_length, 'big')).decode('utf-8')
    dp        = base64.b64encode((key.d % (key.p - 1)).to_bytes(half_modulus_length, 'big')).decode('utf-8')
    dq        = base64.b64encode((key.d % (key.q - 1)).to_bytes(half_modulus_length, 'big')).decode('utf-8')
    inverse_q = base64.b64encode(pow(key.q, -1, key.p).to_bytes(half_modulus_length, 'big')).decode('utf-8')
    d         = base64.b64encode(key.d.to_bytes(modulus_length, 'big')).decode('utf-8')
    return (f"<RSAKeyValue><Modulus>{modulus}</Modulus><Exponent>{exponent}</Exponent>"
            f"<P>{p}</P><Q>{q}</Q><DP>{dp}</DP><DQ>{dq}</DQ>"
            f"<InverseQ>{inverse_q}</InverseQ><D>{d}</D></RSAKeyValue>")

def generate_public_key_xml(key):
    public_key = key.publickey()
    return public_key_to_xml(public_key)

def generate_private_key_xml(key):
    return private_key_to_xml(key)

# Generate RSA keys
key = RSA.generate(2048)

# Export public key
public_key_xml = generate_public_key_xml(key)
with open('publickey.xml', 'w') as file:
    file.write(public_key_xml)
print(f"Public key written to: publickey.xml")
print(public_key_xml)

# Export private key
private_key_xml = generate_private_key_xml(key)
with open('privatekey.xml', 'w') as file:
    file.write(private_key_xml)
print(f"\nPrivate key written to: privatekey.xml")
print(private_key_xml)
