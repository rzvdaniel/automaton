from Crypto.PublicKey import RSA
import base64

def public_key_to_xml(key):
    return f"""<RSAKeyValue>
<Modulus>{base64.b64encode(key.n.to_bytes((key.n.bit_length() + 7) // 8, 'big')).decode('utf-8')}</Modulus>
<Exponent>{base64.b64encode(key.e.to_bytes((key.e.bit_length() + 7) // 8, 'big')).decode('utf-8')}</Exponent>
</RSAKeyValue>"""

def private_key_to_xml(key):
    modulus_length = (key.n.bit_length() + 7) // 8
    half_modulus_length = (modulus_length + 1) // 2
    return f"""<RSAKeyValue>
<Modulus>{base64.b64encode(key.n.to_bytes(modulus_length, 'big')).decode('utf-8')}</Modulus>
<Exponent>{base64.b64encode(key.e.to_bytes((key.e.bit_length() + 7) // 8, 'big')).decode('utf-8')}</Exponent>
<P>{base64.b64encode(key.p.to_bytes(half_modulus_length, 'big')).decode('utf-8')}</P>
<Q>{base64.b64encode(key.q.to_bytes(half_modulus_length, 'big')).decode('utf-8')}</Q>
<DP>{base64.b64encode((key.d % (key.p - 1)).to_bytes(half_modulus_length, 'big')).decode('utf-8')}</DP>
<DQ>{base64.b64encode((key.d % (key.q - 1)).to_bytes(half_modulus_length, 'big')).decode('utf-8')}</DQ>
<InverseQ>{base64.b64encode(key.u.to_bytes(half_modulus_length, 'big')).decode('utf-8')}</InverseQ>
<D>{base64.b64encode(key.d.to_bytes(modulus_length, 'big')).decode('utf-8')}</D>
</RSAKeyValue>"""

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

# Export private key
private_key_xml = generate_private_key_xml(key)
with open('privatekey.xml', 'w') as file:
    file.write(private_key_xml)
