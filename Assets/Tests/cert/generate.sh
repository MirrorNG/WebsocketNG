#!/bin/bash

# https://jamielinux.com/docs/openssl-certificate-authority/sign-server-and-client-certificates.html

echo Generate CA Certificate

#Generate private Key
openssl genrsa -out CA.key 2048

#Generate CA Certificate (100 years)
openssl req -config openssl.cnf \
    -key CA.key \
    -new -sha256 -x509 -extensions v3_ca \
    -subj "/C=US/ST=Texas/O=Mirage/CN=MIRRORNG CA" \
    -out CA.pem 

openssl x509 -noout -text -in CA.pem

# clear database
echo -n > index.txt
echo "1000" > serial
#--------------------------------------------------------------------------------------

echo Generate Intermediary CA Certificate

#Generate private Key
openssl genrsa -out CA_Intermediary.key 2048

#Create Intermediary CA CSR
openssl req -config openssl_int.cnf \
    -new -sha256 \
    -subj "/C=US/ST=Texas/O=Mirage/CN=MIRRORNG INTERMEDIARY CA" \
    -out CA_Intermediary.csr \
    -key CA_Intermediary.key \

# Sign with root CA
openssl ca -config openssl.cnf \
    -in CA_Intermediary.csr \
    -out CA_Intermediary.crt \
    -days 36500 \
    -batch \
    -extensions v3_intermediate_ca

echo Intermediary Certificate
openssl x509 -noout -text \
    -in CA_Intermediary.crt

openssl verify -CAfile CA.pem CA_Intermediary.crt

cat CA_Intermediary.crt CA.pem > CA_Chain.pem

#--------------------------------------------------------------------------------------

echo Generate Server Certificate signed by Intermediary CA

#Generate private Key
openssl genrsa -out localhost.key 2048

#Ceate Server CSR
openssl req -config openssl_int.cnf \
    -new -sha256 \
    -key localhost.key \
    -out localhost.csr \
    -subj "/C=US/ST=Texas/O=Mirage/CN=localhost"

#Generate Server Certificate
openssl ca -config openssl_int.cnf \
    -extensions server_cert \
    -days 36500 \
    -notext \
    -md sha256 \
    -in localhost.csr \
    -out localhost.crt \
    -batch

#openssl x509 -req -in ServerCert_signedByCAIntermediary.csr -CA CA_Intermediary.crt -CAkey CA_Intermediary.key -CAcreateserial -out ServerCert_signedByCAIntermediary.crt -days 36500 -sha256

#View Certificate
openssl x509 -text -noout -in localhost.crt

openssl verify -CAfile CA_chain.pem \
    localhost.crt

#Generate chain
cat localhost.crt CA_Intermediary.crt CA.pem > localhost_chain.crt

#Package in a pfx
openssl pkcs12 -export -out localhost.pfx -inkey localhost.key -in localhost_chain.crt -passout "pass:password"