from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.backends import default_backend
import base64

def b64(n, length):
    return base64.b64encode(n.to_bytes(length, 'big')).decode('utf-8')

def int_byte_length(n):
    return (n.bit_length() + 7) // 8

def public_key_to_xml(pub):
    n = pub.public_numbers()
    modulus  = b64(n.n, int_byte_length(n.n))
    exponent = b64(n.e, int_byte_length(n.e))
    return f"<RSAKeyValue><Modulus>{modulus}</Modulus><Exponent>{exponent}</Exponent></RSAKeyValue>"

def private_key_to_xml(priv):
    n = priv.private_numbers()
    pub = n.public_numbers
    mod_len  = int_byte_length(pub.n)
    half_len = (mod_len + 1) // 2
    modulus   = b64(pub.n,  mod_len)
    exponent  = b64(pub.e,  int_byte_length(pub.e))
    p         = b64(n.p,    half_len)
    q         = b64(n.q,    half_len)
    dp        = b64(n.dmp1, half_len)
    dq        = b64(n.dmq1, half_len)
    inverse_q = b64(n.iqmp, half_len)  # cryptography lib stores q⁻¹ mod p correctly
    d         = b64(n.d,    mod_len)
    return (f"<RSAKeyValue><Modulus>{modulus}</Modulus><Exponent>{exponent}</Exponent>"
            f"<P>{p}</P><Q>{q}</Q><DP>{dp}</DP><DQ>{dq}</DQ>"
            f"<InverseQ>{inverse_q}</InverseQ><D>{d}</D></RSAKeyValue>")

# Generate RSA keys
private_key = rsa.generate_private_key(
    public_exponent=65537,
    key_size=2048,
    backend=default_backend()
)
public_key = private_key.public_key()

# Export public key
public_key_xml = public_key_to_xml(public_key)
with open('publickey.xml', 'w') as f:
    f.write(public_key_xml)
print("Public key written to: publickey.xml")
print(public_key_xml)

# Export private key
private_key_xml = private_key_to_xml(private_key)
with open('privatekey.xml', 'w') as f:
    f.write(private_key_xml)
print("\nPrivate key written to: privatekey.xml")
print(private_key_xml)

