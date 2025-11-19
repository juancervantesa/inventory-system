# Sistema de Inventario con Arquitectura de Microservicios y Kafka

Este proyecto implementa un sistema de inventario básico utilizando una arquitectura de microservicios, orquestada con Docker Compose. La comunicación entre servicios se realiza de forma asíncrona a través de un clúster de Apache Kafka para garantizar la resiliencia y la consistencia de los datos.

## Arquitectura

El sistema se compone de los siguientes servicios:

-   **Products Service**: Gestiona la información de los productos, incluido el stock.
-   **Orders Service**: Gestiona la creación de pedidos.
-   **Load Balancer**: Un balanceador de carga Nginx que distribuye el tráfico entre las instancias del `Products Service`.
-   **Bases de datos PostgreSQL**: Dos bases de datos aisladas, una para cada servicio (`products-db` y `orders-db`).
-   **Clúster de Kafka**: Utilizado para la comunicación asíncrona entre servicios.
    -   **Zookeeper**: Requerido para la gestión del clúster de Kafka.
    -   **Kafka**: El broker de mensajes que almacena los eventos.
    -   **Kafka UI**: Una interfaz de usuario web para visualizar y gestionar el clúster de Kafka.

## Autenticación de Servicio a Servicio con Keycloak

Se ha implementado un sistema de autenticación de servicio a servicio utilizando **Keycloak** y el flujo de **Client Credentials (OAuth2)** para asegurar las comunicaciones entre `Orders.Service` y `Products.Service`.

### Configuración de Keycloak

*   **Servicio Keycloak**: Incluido en `docker-compose.yml` (`keycloak`).
*   **Pre-configuración**: Keycloak se inicializa automáticamente con un realm (`inventory-system-realm`) y clientes predefinidos a través del archivo `keycloak/realm.json`.
*   **Clientes**:
    *   `orders-service-client`: Cliente confidencial utilizado por `Orders.Service` para obtener tokens.
    *   `products-service-client`: Cliente "bearer-only" que actúa como audiencia para los tokens destinados a `Products.Service`.
*   **Mapeador de Audiencia**: El `orders-service-client` tiene un `protocolMapper` configurado en `realm.json` para asegurar que los tokens emitidos incluyan `products-service-client` en su claim de audiencia (`aud`).

### `Orders.Service` (Cliente de Autenticación)

*   **Rol**: Actúa como cliente que solicita tokens de acceso a Keycloak.
*   **Mecanismo**: Utiliza un `AuthenticationDelegatingHandler` que intercepta las llamadas salientes a `Products.Service`. Este handler:
    *   Obtiene un token de acceso de Keycloak (si no tiene uno válido o ha expirado).
    *   Añade el token al encabezado `Authorization: Bearer <token>` de la petición.
*   **Configuración**: Las credenciales y URLs de Keycloak se inyectan a través de variables de entorno (`Keycloak__Authority`, `Keycloak__TokenEndpoint`, `Keycloak__ClientId`, `Keycloak__ClientSecret`, `Keycloak__ProductsAudience`).

### `Products.Service` (Servidor de Recursos)

*   **Rol**: Actúa como servidor de recursos, validando los tokens JWT recibidos.
*   **Mecanismo**: Configurado con el middleware de autenticación `JwtBearer` de ASP.NET Core.
*   **Protección**: Los endpoints de `ProductsController` están protegidos con el atributo `[Authorize]`, requiriendo un token válido.
*   **Configuración**: Valida el emisor (`issuer`) y la audiencia (`audience`) del token basándose en las variables de entorno (`Keycloak__Authority`, `Keycloak__Audience`).
*   **Nota de Desarrollo**: Para facilitar el desarrollo local con HTTP, la validación del emisor está deshabilitada (`ValidateIssuer = false`).

---

## Implementación de Arquitectura Orientada a Eventos con Kafka

Para resolver problemas de consistencia de datos y mejorar el desacoplamiento entre los servicios, se ha implementado una comunicación basada en eventos.

### Problema Original: Condición de Carrera

En la comunicación síncrona inicial, el `Orders Service` primero consultaba el stock del `Products Service` y luego creaba el pedido. Este enfoque de "verificar y luego actuar" es propenso a condiciones de carrera: dos pedidos podían verificar el stock simultáneamente y ser procesados, incluso si el stock combinado superaba el disponible.

### Solución: Comunicación Asíncrona

Se reemplazo la comunicación síncrona con actualización de stock síncrona por un flujo de eventos asíncrono, que garantiza que las actualizaciones de inventario sean atómicas y se procesen en orden.

El nuevo flujo funciona de la siguiente manera:

1.  **Creación del Pedido**: Un cliente envía una solicitud `POST` al `Orders Service` para crear un nuevo pedido.
2.  **Validación y Publicación del Evento**:
    -   El `Orders Service` realiza una validación inicial (ej. que el producto exista).
    -   Guarda el nuevo pedido en su propia base de datos.
    -   Publica un evento `OrderPlaced` en el topic de Kafka `orders_topic`. El mensaje contiene el `ProductId` y la `Quantity` del pedido.
3.  **Consumo del Evento y Actualización de Stock**:
    -   El `Products Service` está suscrito al topic `orders_topic`.
    -   Al recibir un mensaje `OrderPlaced`, el servicio consume el evento.
    -   Procede a **actualizar el stock** del producto correspondiente en su propia base de datos de forma atómica (`stock = stock - quantity`).

Este patrón asegura que la responsabilidad de actualizar el stock recaiga únicamente en el `Products Service`, eliminando la condición de carrera y haciendo el sistema más robusto.

---

## Cómo Ejecutar el Proyecto

### Prerrequisitos

-   Tener Docker y Docker Compose instalados.
-   Se recomienda usar una herramienta como Postman para probar las APIs.

### Pasos para la Ejecución

1.  Clona este repositorio en tu máquina local.
2.  Abre una terminal en la raíz del proyecto.
3.  Ejecuta el siguiente comando para levantar todos los servicios. El flag `--build` es crucial para asegurar que los últimos cambios en el código de los servicios se apliquen.

    ```shell
    docker-compose up --build -d
    ```
    *Nota: Keycloak puede tardar un poco en inicializarse y cargar el realm por primera vez.*

### Acceso a los Servicios

Una vez que los contenedores estén en funcionamiento, puedes acceder a los diferentes componentes del sistema:

-   **Keycloak Admin Console**: `http://localhost:8080` (Usuario: `admin`, Contraseña: `admin`)
-   **API de Productos**: `http://localhost:8082/api/products` (a través del balanceador de carga)
-   **API de Pedidos**: `http://localhost:8083/api/orders`
-   **Interfaz de Usuario de Kafka**: `http://localhost:8090`

---

## Cómo Probar la Aplicación y la Autenticación

Para verificar que la aplicación y la autenticación funcionan correctamente, sigue estos pasos usando Postman:

### 1. Obtener Token de Acceso (Manual)

Para interactuar con los endpoints protegidos de `Products.Service` directamente desde Postman, primero necesitas un token de acceso.

*   **Petición**: `POST`
*   **URL**: `http://localhost:8080/realms/inventory-system-realm/protocol/openid-connect/token`
*   **Body (x-www-form-urlencoded)**:
    *   `client_id`: `orders-service-client`
    *   `client_secret`: `orders-secret`
    *   `grant_type`: `client_credentials`
    *   `resource`: `products-service-client`
*   **Acción**: Envía la petición y copia el `access_token` de la respuesta.

### 2. Crear/Consultar un Producto (Endpoint Protegido)

Usa el token obtenido en el paso anterior para interactuar con `Products.Service`.

*   **Petición (Ej. Crear Producto)**: `POST`
*   **URL**: `http://localhost:8082/api/products`
*   **Authorization**: `Bearer Token` (pega el `access_token` copiado).
*   **Body (raw, JSON)**:
    ```json
    {
        "name": "Producto de Prueba",
        "price": 99.99,
        "stock": 100
    }
    ```
*   **Acción**: Envía la petición. Deberías recibir un `201 Created`.

    *También puedes usar este token para `GET http://localhost:8082/api/products` o `GET http://localhost:8082/api/products/{id}`.*

### 3. Crear una Orden (Integración con Autenticación Automática)

Esta prueba verifica la comunicación de servicio a servicio con autenticación automática.

*   **Petición**: `POST`
*   **URL**: `http://localhost:8083/api/orders`
*   **Authorization**: **`No Auth`** (¡Importante! `Orders.Service` gestiona su propia autenticación).
*   **Body (raw, JSON)**:
    ```json
    {
        "productId": 1,
        "quantity": 5
    }
    ```
    *(Asegúrate de que el `productId` exista, por ejemplo, el que creaste en el paso anterior).*
*   **Acción**: Envía la petición.
*   **Resultado esperado**: Deberías recibir un `201 Created`. Esto confirma que `Orders.Service` obtuvo su propio token de Keycloak y lo usó exitosamente para llamar a `Products.Service`.

### 4. Verificar la Actualización del Stock

*   Envía una petición `GET` a `http://localhost:8082/api/products/1` (usando el token del Paso 1).
*   El stock del producto debería haberse reducido según la cantidad del pedido.